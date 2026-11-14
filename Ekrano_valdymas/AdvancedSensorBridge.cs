using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ekrano_valdymas
{
    internal sealed record AdvancedSensorSnapshot(
        double? CpuTemperature,
        double? CpuPower,
        double? CpuVoltage,
        double? CpuFan,
        double? GpuTemperature,
        double? GpuHotspot,
        double? GpuPower,
        double? GpuFan,
        double? GpuVoltage, StorageInfo[]? Storage=null,string? GpuId=null,string GpuPowerScope="GPU",double? CpuPump=null,double? GpuPump=null,HardwareReading[]? Sensors=null,
        IReadOnlyDictionary<SystemMonitorWidget.MetricKind,double?>? Metrics=null,GpuAdapterChoice[]? Adapters=null,HardwareAccessStatus? Access=null)
    {
        internal bool HasHardwareSnapshot => HardwareMetricValues.IsComplete(Metrics) && Metrics!.Values.Any(v=>v.HasValue) && Sensors is {Length:>0} && Adapters!=null;
    }

    internal static class AdvancedSensorServer
    {
        private static StorageInfo[] storage=[];
        private static DateTime nextStorageRead;
        internal const string PipeName = "BlakiDeck.AdvancedSensors.v1";

        public static async Task RunAsync(string? requestedPipe=null)
        {
            string pipeName=requestedPipe??PipeName;
            using Mutex mutex = new(true, @"Local\"+pipeName+".Helper", out bool created);
            if (!created) return;
            Computer computer = new()
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true
            };
            try
            {
                using NamedPipeClientStream pipe = new(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(15000);
                var access = HardwareAccessStatus.Check();
                try { computer.Open(); }
                catch (Exception error)
                {
                    access = access with { InitializationProblem = access.Problem ?? "Advanced sensor initialization failed. Retry sensor access; individual readings may remain unavailable on unsupported hardware." };
                    InteractionDiagnostics.RecordFailure("Advanced sensors initialization", error);
                }
                using StreamReader reader = new(pipe);
                using StreamWriter writer = new(pipe) { AutoFlush = true };
                while (pipe.IsConnected)
                {
                    string? command = await reader.ReadLineAsync();
                    if (command == null || command.Equals("EXIT", StringComparison.OrdinalIgnoreCase)) break;
                    if(command.StartsWith("MODE ",StringComparison.Ordinal))
                    {
                        _=NativeEcoQoS.SetCurrentHelper(!command.EndsWith("Normal",StringComparison.Ordinal));
                        continue;
                    }
                    bool storageRequested=command=="READ STORAGE";
                    if (!storageRequested&&!command.Equals("READ", StringComparison.OrdinalIgnoreCase)) continue;
                    await writer.WriteLineAsync(JsonSerializer.Serialize(Read(computer,storageRequested) with { Access=access }));
                }
            }
            catch (IOException) { /* Parent closed its pipe during shutdown. */ }
            finally { try { computer.Close(); } catch { } }
        }

        private static AdvancedSensorSnapshot Read(Computer computer,bool storageRequested)
        {
            List<(HardwareType Hardware, ISensor Sensor)> sensors = new();
            string? sampledGpuId=null;
            try
            {
                var selected=GpuTelemetrySelection.Pick(computer.Hardware);sampledGpuId=selected?.Identifier.ToString();
                foreach (IHardware hardware in computer.Hardware)
                    ReadHardware(hardware, sensors);
            }
            catch { sensors.Clear(); }
            var catalogue=HardwareTelemetry.Capture(sensors);
            sensors.RemoveAll(s=>GpuTelemetrySelection.IsGpu(s.Hardware)&&(sampledGpuId==null||!s.Sensor.Identifier.ToString().StartsWith(sampledGpuId+"/",StringComparison.Ordinal)));
            if(storageRequested&&DateTime.UtcNow>=nextStorageRead){storage=StorageInfoReader.Read();nextStorageRead=DateTime.UtcNow.AddSeconds(30);}
            return new AdvancedSensorSnapshot(
                Find(sensors, IsCpu, SensorType.Temperature, "CPU Package", "Package", "Core (Tctl/Tdie)", "Tctl"),
                Find(sensors, IsCpu, SensorType.Power, "CPU Package", "Package", "Total"),
                Find(sensors, IsCpu, SensorType.Voltage, "CPU Core", "Vcore", "Core"),
                Find(sensors, _ => true, SensorType.Fan, "CPU Fan", "CPU_FAN"),
                Find(sensors, IsGpu, SensorType.Temperature, "GPU Core", "Core"),
                Find(sensors, IsGpu, SensorType.Temperature, "GPU Hot Spot", "Hot Spot", "Hotspot"),
                GpuTelemetrySelection.Power(sensors).Value,
                Find(sensors, IsGpu, SensorType.Fan, "GPU Fan", "Fan"),
                Find(sensors, IsGpu, SensorType.Voltage, "GPU Core Voltage", "GPU Core", "Core"),storageRequested?storage:null,sampledGpuId,GpuTelemetrySelection.Power(sensors).Scope,GpuTelemetrySelection.Pump(sensors,false),GpuTelemetrySelection.Pump(sensors,true),catalogue,
                HardwareMetricValues.Capture(sensors),GpuTelemetrySelection.Adapters);
        }

        private static void ReadHardware(
            IHardware hardware,
            ICollection<(HardwareType Hardware, ISensor Sensor)> sensors)
        {
            hardware.Update();
            foreach (ISensor sensor in hardware.Sensors)
            {
                // The client keeps its own graph history; this helper supplies current values only.
                if(sensor.ValuesTimeWindow!=TimeSpan.Zero)sensor.ValuesTimeWindow=TimeSpan.Zero;
                sensors.Add((hardware.HardwareType, sensor));
            }
            foreach (IHardware child in hardware.SubHardware) ReadHardware(child, sensors);
        }

        private static double? Find(
            IEnumerable<(HardwareType Hardware, ISensor Sensor)> all,
            Func<HardwareType, bool> filter,
            SensorType type,
            params string[] preferred)
        {
            return GpuTelemetrySelection.Find(all,filter,type,preferred);
        }

        private static bool IsCpu(HardwareType type) => type == HardwareType.Cpu;
        private static bool IsNvidia(HardwareType type) => type == HardwareType.GpuNvidia;
        private static bool IsGpu(HardwareType type) =>
            type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
    }

    internal sealed class AdvancedSensorClient : IDisposable
    {
        private readonly object sync = new();
        private readonly CancellationTokenSource cancellation = new();
        private DateTime receivedAt;
        private AdvancedSensorSnapshot latest = new(null, null, null, null, null, null, null, null, null);
        private readonly Task polling;
        private readonly Task helperLaunch;
        private int disposed;
        private string? helperProblem;
        private bool connected;
        private DateTime connectedAt;
        private readonly TaskCompletionSource connectionEstablished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string pipeName;
        internal volatile bool IncludeStorage;
        internal Task Completion => Task.WhenAll(polling,helperLaunch);

        public AdvancedSensorClient() : this(AdvancedSensorServer.PipeName+"."+Guid.NewGuid().ToString("N"), true) { }
        internal AdvancedSensorClient(string pipeName, bool startHelper, Action? launchOverride=null)
        {
            this.pipeName = pipeName;
            polling = Task.Run(PollAsync);
            var token=cancellation.Token;
            // ShellExecute(runas) may pump native messages while waiting for the
            // shell/UAC. Never invoke it on WPF's layout/input thread, including
            // when a visible sensor is resumed inside DisableProcessing().
            helperLaunch=startHelper?Task.Run(async ()=>
            {
                if(token.IsCancellationRequested)return;
                try
                {
                    if(launchOverride!=null)launchOverride();else StartElevatedHelper(pipeName);
                    // UAC can legitimately remain open; only time out after the user's decision.
                    await connectionEstablished.Task.WaitAsync(TimeSpan.FromSeconds(30),token);
                }
                catch(OperationCanceledException) when(token.IsCancellationRequested) { }
                catch(TimeoutException)
                {
                    lock(sync) helperProblem="The advanced sensor helper did not connect. Use Sensor support to retry or repair BlakiDeck if Windows blocked its components.";
                    cancellation.Cancel();
                }
                catch(System.ComponentModel.Win32Exception error) when(error.NativeErrorCode==1223)
                {
                    lock(sync) helperProblem="Administrator access was declined. Basic readings remain available. Use Sensor support to retry advanced readings.";
                    cancellation.Cancel();
                }
                catch(Exception error)
                {
                    lock(sync) helperProblem="The advanced sensor helper could not start. Use Sensor support to retry and check Windows security notifications.";
                    InteractionDiagnostics.RecordFailure("Advanced sensor helper launch",error);
                    cancellation.Cancel();
                }
            }):Task.CompletedTask;
        }

        public AdvancedSensorSnapshot Read()=>Read(out _);
        internal string? Problem
        {
            get { lock(sync) return helperProblem ?? latest.Access?.Problem ?? (connected&&(receivedAt==default?DateTime.UtcNow-connectedAt>TimeSpan.FromSeconds(60):DateTime.UtcNow-receivedAt>TimeSpan.FromSeconds(5))?"The advanced sensor helper is not responding. Use Sensor support to retry.":null); }
        }
        internal string SupportSummary
        {
            get { lock(sync) return Problem ?? (receivedAt==default?(connected?"Initializing advanced hardware sensors…":"Waiting for advanced sensor access. If Windows is asking for administrator permission, complete that prompt."):"Advanced sensor access is available. Individual readings still depend on the hardware and its drivers."); }
        }
        internal AdvancedSensorSnapshot Read(out DateTime sampledAt)
        {
            lock (sync){sampledAt=receivedAt;return DateTime.UtcNow-receivedAt<TimeSpan.FromSeconds(5)?latest:new(null,null,null,null,null,null,null,null,null);}
        }

        private static void StartElevatedHelper(string pipeName)
        {
                string executable = Environment.ProcessPath ?? "BlakiDeck.exe";
                using var launched=Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--sensor-helper "+pipeName,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                }) ?? throw new InvalidOperationException("The sensor helper did not start.");
        }

        private async Task PollAsync()
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                lock(sync){connected=true;connectedAt=DateTime.UtcNow;}
                connectionEstablished.TrySetResult();
                using StreamReader reader = new(pipe, leaveOpen: true);
                // Only this task owns stream I/O and disposal. Cancellation interrupts
                // pending reads/writes; closing the pipe tells the helper to exit.
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = false };
                ResourceMode? previousMode=null;
                while (!cancellation.IsCancellationRequested && pipe.IsConnected)
                {
                    if(previousMode!=ResourcePolicy.Mode)
                    {
                        previousMode=ResourcePolicy.Mode;
                        await writer.WriteLineAsync(("MODE "+previousMode).AsMemory(),cancellation.Token);
                    }
                    await writer.WriteLineAsync((IncludeStorage?"READ STORAGE":"READ").AsMemory(), cancellation.Token);
                    await writer.FlushAsync(cancellation.Token);
                    string? json = await reader.ReadLineAsync(cancellation.Token);
                    if (json == null) break;
                    AdvancedSensorSnapshot? snapshot = JsonSerializer.Deserialize<AdvancedSensorSnapshot>(json);
                    if (snapshot != null) lock (sync){latest=snapshot;receivedAt=DateTime.UtcNow;HardwareTelemetry.Publish(snapshot.Sensors??[],true);if(snapshot.HasHardwareSnapshot)GpuTelemetrySelection.PublishAdapters(snapshot.Adapters!);}
                    await Task.Delay(1000, cancellation.Token);
                }
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
            catch(Exception error)
            {
                if(!cancellation.IsCancellationRequested)
                {
                    lock(sync) helperProblem="The advanced sensor connection was interrupted. Use Sensor support to retry.";
                    InteractionDiagnostics.RecordFailure("Advanced sensor connection",error);
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            cancellation.Cancel();
            // Never wait on I/O on the UI shutdown thread or dispose its token early.
            _ = Completion.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        }
    }
}
