using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ekrano_valdymas
{
    internal sealed class ObsWebSocketClient : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cancellation;
        private int _requestNumber;

        public event EventHandler<ObsStateChangedEventArgs>? StateChanged;

        public bool IsConnected => _socket?.State == WebSocketState.Open;
        public TimeSpan? RecordTimecode { get; private set; }

        public async Task ConnectAsync()
        {
            Dispose();

            _cancellation = new CancellationTokenSource();
            CancellationToken connectionToken = _cancellation.Token;
            // Installation/config discovery can inspect the running OBS process;
            // keep it off the WPF dispatcher.
            ObsConnectionSettings settings = await Task.Run(LoadSettings, connectionToken);
            connectionToken.ThrowIfCancellationRequested();
            if (!settings.Enabled)
                throw new ObsWebSocketDisabledException();

            _socket = new ClientWebSocket();

            await _socket.ConnectAsync(
                new Uri($"ws://127.0.0.1:{settings.Port}"),
                connectionToken);

            JsonElement hello = await ReceiveMessageAsync(connectionToken);
            JsonElement helloData = hello.GetProperty("d");

            string? authentication = null;
            if (helloData.TryGetProperty("authentication", out JsonElement auth))
            {
                string challenge = auth.GetProperty("challenge").GetString() ?? "";
                string salt = auth.GetProperty("salt").GetString() ?? "";
                authentication = CreateAuthentication(settings.Password, salt, challenge);
            }

            object identifyData = authentication == null
                ? new { rpcVersion = 1, eventSubscriptions = 64 }
                : new { rpcVersion = 1, authentication, eventSubscriptions = 64 };

            await SendMessageAsync(new { op = 1, d = identifyData }, connectionToken);

            JsonElement identified = await ReceiveMessageAsync(connectionToken);
            if (identified.GetProperty("op").GetInt32() != 2)
                throw new InvalidOperationException("OBS WebSocket authentication failed.");

            _ = Task.Run(() => ReceiveLoopAsync(connectionToken));
        }

        public async Task<JsonElement> SendRequestAsync(string requestType)
        {
            if (_socket?.State != WebSocketState.Open || _cancellation == null)
                throw new InvalidOperationException("OBS WebSocket is not connected.");

            string requestId = Interlocked.Increment(ref _requestNumber).ToString();
            TaskCompletionSource<JsonElement> completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            _pendingRequests[requestId] = completion;

            await SendMessageAsync(new
            {
                op = 6,
                d = new
                {
                    requestType,
                    requestId
                }
            }, _cancellation.Token);

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    JsonElement message = await ReceiveMessageAsync(cancellationToken);
                    int opCode = message.GetProperty("op").GetInt32();
                    JsonElement data = message.GetProperty("d");

                    try
                    {
                        if (opCode == 7)
                            HandleRequestResponse(data);
                        else if (opCode == 5)
                            HandleEvent(data);
                    }
                    catch (JsonException)
                    {
                        // Nežinomas OBS įvykio formatas neturi nutraukti viso ryšio.
                    }
                    catch (InvalidOperationException)
                    {
                        // Ignoruojame tik netikėtą atskiro pranešimo struktūrą.
                    }
                }
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
                _socket?.Abort();
                foreach (TaskCompletionSource<JsonElement> pending in _pendingRequests.Values)
                    pending.TrySetException(new InvalidOperationException(
                        "OBS WebSocket connection was interrupted."));
                _pendingRequests.Clear();
                StateChanged?.Invoke(this, new ObsStateChangedEventArgs(connected: false));
            }
        }

        private void HandleRequestResponse(JsonElement data)
        {
            string requestId = data.GetProperty("requestId").GetString() ?? "";
            if (!_pendingRequests.TryRemove(requestId, out TaskCompletionSource<JsonElement>? completion))
                return;

            JsonElement status = data.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
            {
                int code = status.TryGetProperty("code", out JsonElement statusCode)
                    ? statusCode.GetInt32()
                    : 0;
                string comment = status.TryGetProperty("comment", out JsonElement error)
                    ? error.GetString() ?? "OBS request failed."
                    : "OBS request failed.";
                completion.SetException(new InvalidOperationException(
                    code > 0 ? $"{comment} (OBS code {code})" : comment));
                return;
            }

            JsonElement responseData = data.TryGetProperty("responseData", out JsonElement response)
                ? response.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();

            completion.SetResult(responseData);
        }

        private void HandleEvent(JsonElement data)
        {
            string eventType = data.GetProperty("eventType").GetString() ?? "";
            JsonElement eventData = data.GetProperty("eventData");

            if (eventType == "RecordStateChanged")
            {
                bool active = eventData.GetProperty("outputActive").GetBoolean();
                RecordTimecode = ParseTimecode(eventData);
                StateChanged?.Invoke(this, new ObsStateChangedEventArgs(recordActive: active));
            }
            else if (eventType == "RecordFileChanged")
            {
                RecordTimecode = TimeSpan.Zero;
            }
            else if (eventType == "ReplayBufferStateChanged")
            {
                bool active = eventData.GetProperty("outputActive").GetBoolean();
                StateChanged?.Invoke(this, new ObsStateChangedEventArgs(replayBufferActive: active));
            }
        }

        private static TimeSpan? ParseTimecode(JsonElement eventData)
        {
            if (eventData.TryGetProperty("outputTimecode", out JsonElement timecode) &&
                TimeSpan.TryParse(timecode.GetString(), out TimeSpan parsed))
            {
                return parsed;
            }

            return null;
        }

        private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            await _socket!.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }

        private async Task<JsonElement> ReceiveMessageAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using MemoryStream message = new();
            WebSocketReceiveResult result;

            do
            {
                result = await _socket!.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("OBS WebSocket connection closed.");

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            using JsonDocument document = JsonDocument.Parse(message.ToArray());
            return document.RootElement.Clone();
        }

        private static string CreateAuthentication(string password, string salt, string challenge)
        {
            string secret = Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));

            return Convert.ToBase64String(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
        }

        private static ObsConnectionSettings LoadSettings()
        {
            var installation = ObsInstallationLocator.Resolve();
            string configPath = Path.Combine(installation?.ConfigDirectory ?? ObsInstallationLocator.RoamingConfigDirectory,
                "plugin_config", "obs-websocket", "config.json");
            try
            {
                if (!File.Exists(configPath)) throw new FileNotFoundException();
                using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
                JsonElement root = config.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("server_enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !root.TryGetProperty("server_port", out var port) || port.ValueKind != JsonValueKind.Number || !port.TryGetInt32(out int number) || number is < 1 or > 65535)
                    throw new JsonException();
                return new ObsConnectionSettings(enabled.GetBoolean(), number,
                    root.TryGetProperty("server_password", out var password) && password.ValueKind == JsonValueKind.String ? password.GetString() ?? "" : "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                throw new ObsSetupException("Open OBS → Tools → WebSocket Server Settings, enable the server and apply the settings, then reconnect.",
                    "Atidarykite OBS → Tools → WebSocket Server Settings, įjunkite serverį, išsaugokite nustatymus ir junkitės iš naujo.");
            }
        }

        public void Dispose()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;

            _socket?.Dispose();
            _socket = null;

            foreach (TaskCompletionSource<JsonElement> pending in _pendingRequests.Values)
                pending.TrySetCanceled();

            _pendingRequests.Clear();
        }

        private sealed record ObsConnectionSettings(bool Enabled, int Port, string Password);
    }

    internal sealed class ObsStateChangedEventArgs : EventArgs
    {
        public ObsStateChangedEventArgs(
            bool? recordActive = null,
            bool? replayBufferActive = null,
            bool? connected = null)
        {
            RecordActive = recordActive;
            ReplayBufferActive = replayBufferActive;
            Connected = connected;
        }

        public bool? RecordActive { get; }
        public bool? ReplayBufferActive { get; }
        public bool? Connected { get; }
    }

    internal sealed class ObsWebSocketDisabledException : Exception
    {
    }
}
