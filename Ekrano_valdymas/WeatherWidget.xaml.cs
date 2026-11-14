using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ekrano_valdymas
{
    public partial class WeatherWidget : UserControl
    {
        internal static string FormatForecastDate(DateTime date,string language)
        {
            return new WeatherTranslationStore().FormatDate(date,language);
        }
        private void Header_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || e.LeftButton != MouseButtonState.Pressed) return;
            // Let the shared module-header handler move the complete application.
        }
        private static readonly HttpClient HttpClient = new();
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
        private const int HourlyPageSize = 5;

        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _returnTimer;
        private int _returnSeconds;
        private bool _pageTransitionRunning;
        private readonly string _cacheFile;
        private (double Latitude, double Longitude)? _coordinateOverride;
        private string _selectedCity = "";
        private string _cityQuery = "";
        private bool _locationEnabled=false;
        private int _requestGeneration;
        private int _currentWeatherCode;
        private double _currentApparentTemperature;
        private double _currentTemperature;
        public bool ShowTemperatureDecimals { get; private set; }
        internal static string FormatTemperature(double value,bool decimals,string language)
        {
            CultureInfo culture;
            try{culture=CultureInfo.GetCultureInfo(language);}catch(CultureNotFoundException){culture=CultureInfo.InvariantCulture;}
            double rounded=Math.Round(value,decimals?1:0);
            if(rounded==0)rounded=0; // Avoid displaying negative zero.
            return rounded.ToString(decimals?"F1":"F0",culture)+"°";
        }
        private string TemperatureText(double value)=>FormatTemperature(value,ShowTemperatureDecimals,DisplayLanguage);
        public void SetTemperatureDecimals(bool value)
        {
            ShowTemperatureDecimals=value;
            UpdateConditionWidth();
            if(_forecastDays.Count>0)ShowSelectedDay();
            else if(_hasCurrentWeather){txtCurrentTemp.Text=TemperatureText(_currentTemperature);txtFeelsLike.Text=TemperatureText(_currentApparentTemperature);}
        }
        private bool _hasCurrentWeather;
        private readonly WeatherTranslationStore _translations=new();
        private string _conditionTextKey="Loading...";
        internal event EventHandler? DisplayLanguageChanged;
        private readonly List<WeatherDayForecast> _forecastDays = new();
        private int _selectedDayIndex;
        private int _hourPageStart;
        private Point _dayTouchStart;
        private Point _hourTouchStart;
        private bool _daySwipeHandled;
        private bool _hourSwipeHandled;
        private SwipeZone _activeSwipeZone;

        public bool IsUsingCoordinates => _coordinateOverride.HasValue;

        public string ActiveCoordinatesText =>
            _coordinateOverride is { } coordinates
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{coordinates.Latitude:0.######}, {coordinates.Longitude:0.######}")
                : "";

        public WeatherWidget()
        {
            InitializeComponent();
            daySwipeArea.SizeChanged+=(_,_)=>UpdateConditionWidth();
            currentWeatherIcon.SizeChanged+=(_,_)=>UpdateConditionWidth();
            txtCurrentTemp.SizeChanged+=(_,_)=>UpdateConditionWidth();

            AddHandler(UIElement.PreviewTouchDownEvent,
                new EventHandler<TouchEventArgs>(Weather_PreviewTouchDown), true);
            AddHandler(UIElement.PreviewTouchMoveEvent,
                new EventHandler<TouchEventArgs>(Weather_PreviewTouchMove), true);
            AddHandler(UIElement.PreviewTouchUpEvent,
                new EventHandler<TouchEventArgs>(Weather_PreviewTouchUp), true);

            _cacheFile = Path.Combine(
                DiagnosticState.LocalApplicationData,
                "Ekrano_valdymas",
                "weather-cache.json");

            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                HttpClient.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "Ekrano_valdymas/1.0");
            }

            _refreshTimer = new DispatcherTimer
            {
                Interval = RefreshInterval
            };
            _refreshTimer.Tick += async (_, _) => await RefreshWeatherAsync();
            _returnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _returnTimer.Tick += (_, _) =>
            {
                if (--_returnSeconds <= 0)
                {
                    _returnTimer.Stop();
                    AnimatePageChange(() => { _selectedDayIndex = 0; _hourPageStart = 0; ShowSelectedDay(); }, 1);
                }
                else UpdateReturnCountdown();
            };

            Loaded += WeatherWidget_Loaded;
            Unloaded += WeatherWidget_Unloaded;
            ApplyLanguage();
        }

        internal void SetSavedLocation(WeatherLocation location)
        {
            _locationEnabled=true;
            if(IsLoaded)_refreshTimer.Start();
            ++_requestGeneration; // Invalidate the old location even while unloaded.
            _coordinateOverride = location.Latitude is double lat && location.Longitude is double lon ? (lat, lon) : null;
            _selectedCity = location.Name;
            _cityQuery = string.IsNullOrWhiteSpace(location.AutomaticName) ? location.Name : location.AutomaticName;
            txtCitySearch.Text = location.Name;
            locationOriginIcon.Text = location.Icon;
            locationOriginIcon.TextBrush = location.NameEdited ? AdaptiveSensorColors.PencilBrush : Brushes.DeepSkyBlue;
            locationOriginIcon.Visibility = location.Icon.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            locationButton.ToolTip = location.DescriptionFor(DisplayLanguage);
            System.Windows.Automation.AutomationProperties.SetName(locationButton, location.Name);
            _hasCurrentWeather = false;
            _forecastDays.Clear();
            _returnTimer.Stop();
            _selectedDayIndex = _hourPageStart = 0;
            txtCurrentTemp.Text = "--°";
            txtFeelsLike.Text = "--°";
            txtHumidity.Text = txtRainChance.Text = "--%";
            txtWind.Text = "-- "+Translate("km/h");
            txtSunrise.Text = txtSunset.Text = "--:--";
            txtSelectedDate.Text = "";
            hourlyForecast.ItemsSource = null;
            previousDayButton.IsEnabled = nextDayButton.IsEnabled = false;
            previousHoursButton.IsEnabled = nextHoursButton.IsEnabled = false;
            SetConditionStatus("Loading...");
            if (IsLoaded) _ = RefreshWeatherAsync();
        }
        internal void ClearSavedLocation()
        {
            _locationEnabled=false;++_requestGeneration;_refreshTimer.Stop();_returnTimer.Stop();
            _forecastDays.Clear();_hasCurrentWeather=false;
            txtCitySearch.Text=Translate("Select place");locationOriginIcon.Visibility=Visibility.Collapsed;
            txtCurrentTemp.Text=txtFeelsLike.Text="--°";SetConditionStatus("Add a place in settings");
            txtHumidity.Text=txtRainChance.Text="--%";txtWind.Text="-- "+Translate("km/h");txtSunrise.Text=txtSunset.Text="--:--";
            hourlyForecast.ItemsSource=null;previousDayButton.IsEnabled=nextDayButton.IsEnabled=false;
        }
        internal void SetLocationTranslationTooltip(WeatherLocation location)=>locationButton.ToolTip=location.DescriptionFor(DisplayLanguage);

        private void LocationButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            (Window.GetWindow(this) as MainWindow)?.OpenWeatherLocations(locationButton);
        }

        private async void WeatherWidget_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            LanguageManager.Changed -= LanguageManager_Changed;
            LanguageManager.Changed += LanguageManager_Changed;
            ApplyLanguage();
            _refreshTimer.Start();
            await RefreshWeatherAsync();
        }

        private void WeatherWidget_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _refreshTimer.Stop();
            _returnTimer.Stop();
            LanguageManager.Changed -= LanguageManager_Changed;
        }

        private async Task RefreshWeatherAsync(bool forceRefresh = false)
        {
            if(!_locationEnabled)return;
            int generation = ++_requestGeneration;
            WeatherCache? cache = LoadCache();
            string sourceKey = GetCurrentSourceKey();

            if (!forceRefresh &&
                cache != null &&
                cache.SourceKey == sourceKey &&
                DateTime.UtcNow - cache.SavedAtUtc < RefreshInterval)
            {
                try
                {
                    DisplayWeatherJson(
                        cache.WeatherJson,
                        _selectedCity,
                        generation);
                    return;
                }
                catch
                {
                    // Senesnės programos dviejų dienų talpykloje nėra naujų
                    // valandinių laukų. Tokiu atveju iškart gauname naują formatą.
                }
            }

            try
            {
                if (_coordinateOverride is { } coordinates)
                {
                    await FetchWeatherDataByCoordsAsync(
                        coordinates.Latitude,
                        coordinates.Longitude,
                        _selectedCity,
                        sourceKey,
                        generation);
                }
                else
                {
                    await FetchWeatherDataByCityAsync(
                        _cityQuery,
                        sourceKey,
                        generation);
                }
            }
            catch
            {
                if (cache != null && cache.SourceKey == sourceKey)
                {
                    try
                    {
                        DisplayWeatherJson(
                            cache.WeatherJson,
                            _selectedCity,
                            generation);
                    }
                    catch
                    {
                        Dispatcher.Invoke(() => { if (generation == _requestGeneration) SetConditionStatus("Offline"); });
                    }
                }
                else
                {
                    Dispatcher.Invoke(() => { if (generation == _requestGeneration) SetConditionStatus("Offline"); });
                }
            }
        }

        private async void TxtCitySearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;

            if (_coordinateOverride != null)
            {
                await RefreshWeatherAsync(forceRefresh: true);
                return;
            }

            string city = txtCitySearch.Text.Trim();
            if (string.IsNullOrEmpty(city))
                return;

            _selectedCity = city;
            await RefreshWeatherAsync(forceRefresh: true);
        }

        private async Task FetchWeatherDataByCityAsync(
            string city,
            string sourceKey,
            int generation)
        {
            string geoUrl =
                "https://geocoding-api.open-meteo.com/v1/search" +
                $"?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";
            string geoResponse = await HttpClient.GetStringAsync(geoUrl);
            using JsonDocument geoDocument = JsonDocument.Parse(geoResponse);

            if (!geoDocument.RootElement.TryGetProperty(
                    "results",
                    out JsonElement results) ||
                results.GetArrayLength() == 0)
            {
                Dispatcher.Invoke(() => { if (generation == _requestGeneration) SetConditionStatus("Not found"); });
                return;
            }

            JsonElement result = results[0];
            double latitude = result.GetProperty("latitude").GetDouble();
            double longitude = result.GetProperty("longitude").GetDouble();
            string officialName = result.GetProperty("name").GetString() ?? city;
            if (generation != _requestGeneration) return;

            await FetchWeatherDataByCoordsAsync(
                latitude,
                longitude,
                _selectedCity,
                sourceKey,
                generation);
        }

        private async Task FetchWeatherDataByCoordsAsync(
            double latitude,
            double longitude,
            string displayName,
            string sourceKey,
            int generation)
        {
            string weatherUrl =
                "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
                $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m" +
                "&hourly=temperature_2m,relative_humidity_2m,apparent_temperature,wind_speed_10m,precipitation_probability,weather_code" +
                "&daily=weather_code,temperature_2m_max,apparent_temperature_max,precipitation_probability_max,wind_speed_10m_max,sunrise,sunset" +
                "&forecast_days=3&timezone=auto";

            string weatherResponse = await HttpClient.GetStringAsync(weatherUrl);

            if (generation != _requestGeneration)
                return;

            DisplayWeatherJson(weatherResponse, displayName, generation);
            SaveCache(new WeatherCache(
                sourceKey,
                displayName,
                weatherResponse,
                DateTime.UtcNow));
        }

        private void DisplayWeatherJsonLegacy(
            string weatherJson,
            string displayName,
            int generation)
        {
            if (generation != _requestGeneration)
                return;

            using JsonDocument weatherDocument = JsonDocument.Parse(weatherJson);
            JsonElement current = weatherDocument.RootElement.GetProperty("current");
            JsonElement daily = weatherDocument.RootElement.GetProperty("daily");
            JsonElement hourly = weatherDocument.RootElement.GetProperty("hourly");

            double currentTemperature =
                current.GetProperty("temperature_2m").GetDouble();
            double apparentTemperature =
                current.GetProperty("apparent_temperature").GetDouble();
            int humidity =
                current.GetProperty("relative_humidity_2m").GetInt32();
            double windSpeed =
                current.GetProperty("wind_speed_10m").GetDouble();
            int currentCode =
                current.GetProperty("weather_code").GetInt32();
            DateTime currentTime = DateTime.Parse(
                current.GetProperty("time").GetString()!,
                CultureInfo.InvariantCulture);

            JsonElement hourlyTimes = hourly.GetProperty("time");
            JsonElement hourlyTemperatures = hourly.GetProperty("temperature_2m");
            JsonElement hourlyCodes = hourly.GetProperty("weather_code");
            JsonElement hourlyRain =
                hourly.GetProperty("precipitation_probability");

            int currentHourIndex = Enumerable.Range(
                    0,
                    hourlyTimes.GetArrayLength())
                .FirstOrDefault(index =>
                    DateTime.Parse(
                        hourlyTimes[index].GetString()!,
                        CultureInfo.InvariantCulture) >= currentTime);

            int rainChance =
                hourlyRain[currentHourIndex].ValueKind == JsonValueKind.Null
                    ? 0
                    : hourlyRain[currentHourIndex].GetInt32();

            List<HourlyForecastItem> hourlyItems = new();

            for (int offset = 1;
                 offset <= 6 &&
                 currentHourIndex + offset < hourlyTimes.GetArrayLength();
                 offset++)
            {
                int index = currentHourIndex + offset;
                DateTime forecastTime = DateTime.Parse(
                    hourlyTimes[index].GetString()!,
                    CultureInfo.InvariantCulture);

                hourlyItems.Add(new HourlyForecastItem(
                    forecastTime.ToString("HH:mm"),
                    hourlyCodes[index].GetInt32(),
                    TemperatureText(hourlyTemperatures[index].GetDouble()),hourlyTemperatures[index].GetDouble()));
            }

            string sunrise = FormatTime(
                daily.GetProperty("sunrise")[0].GetString());
            string sunset = FormatTime(
                daily.GetProperty("sunset")[0].GetString());

            Dispatcher.Invoke(() =>
            {
                if (generation != _requestGeneration)
                    return;

                _currentWeatherCode = currentCode;
                _currentApparentTemperature = apparentTemperature;_currentTemperature=currentTemperature;
                _hasCurrentWeather = true;
                txtCitySearch.Text = displayName;
                txtCurrentTemp.Text = TemperatureText(currentTemperature);
                txtCondition.Text = GetWeatherDescription(currentCode);
                txtFeelsLike.Text = TemperatureText(apparentTemperature);
                txtHumidity.Text = $"{humidity}%";
                txtWind.Text = $"{Math.Round(windSpeed)} km/h";
                txtRainChance.Text = $"{rainChance}%";
                txtSunrise.Text = sunrise;
                txtSunset.Text = sunset;
                currentWeatherIcon.WeatherCode = currentCode;
                hourlyForecast.ItemsSource = hourlyItems;
            });
        }

        private void DisplayWeatherJson(string weatherJson, string displayName, int generation)
        {
            if (generation != _requestGeneration) return;
            using JsonDocument document = JsonDocument.Parse(weatherJson);
            JsonElement current = document.RootElement.GetProperty("current");
            JsonElement daily = document.RootElement.GetProperty("daily");
            JsonElement hourly = document.RootElement.GetProperty("hourly");
            DateTime currentTime = DateTime.Parse(current.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
            JsonElement hTimes = hourly.GetProperty("time");
            JsonElement hTemps = hourly.GetProperty("temperature_2m");
            JsonElement hHumidity = hourly.GetProperty("relative_humidity_2m");
            JsonElement hCodes = hourly.GetProperty("weather_code");
            JsonElement hRain = hourly.GetProperty("precipitation_probability");
            JsonElement dTimes = daily.GetProperty("time");
            JsonElement dCodes = daily.GetProperty("weather_code");
            JsonElement dTemps = daily.GetProperty("temperature_2m_max");
            JsonElement dApparent = daily.GetProperty("apparent_temperature_max");
            JsonElement dRain = daily.GetProperty("precipitation_probability_max");
            JsonElement dWind = daily.GetProperty("wind_speed_10m_max");
            JsonElement dSunrise = daily.GetProperty("sunrise");
            JsonElement dSunset = daily.GetProperty("sunset");
            int currentHourIndex = Enumerable.Range(0, hTimes.GetArrayLength())
                .Where(i => DateTime.Parse(hTimes[i].GetString()!, CultureInfo.InvariantCulture) <= currentTime)
                .DefaultIfEmpty(0)
                .Last();
            List<WeatherDayForecast> days = new();

            for (int dayIndex = 0; dayIndex < Math.Min(3, dTimes.GetArrayLength()); dayIndex++)
            {
                DateTime date = DateTime.Parse(dTimes[dayIndex].GetString()!, CultureInfo.InvariantCulture).Date;
                List<HourlyForecastItem> hours = new();
                int representativeHumidity = 0;
                for (int i = 0; i < hTimes.GetArrayLength(); i++)
                {
                    DateTime time = DateTime.Parse(hTimes[i].GetString()!, CultureInfo.InvariantCulture);
                    if (time.Date == date && time.Hour == 12) representativeHumidity = hHumidity[i].GetInt32();
                    // Keep the hourly strip continuous across midnight using
                    // the forecast's local timestamps and existing API order.
                    if (dayIndex == 0 && i <= currentHourIndex) continue;
                    if (dayIndex > 0 && (time.Date < date || time.Date == date && time.Hour < 6)) continue;
                    hours.Add(new HourlyForecastItem(time.ToString("HH:mm"), hCodes[i].GetInt32(),
                        TemperatureText(hTemps[i].GetDouble()),hTemps[i].GetDouble()));
                }

                bool today = dayIndex == 0;
                int rain = today && currentHourIndex < hRain.GetArrayLength()
                    ? (hRain[currentHourIndex].ValueKind == JsonValueKind.Null ? 0 : hRain[currentHourIndex].GetInt32())
                    : dRain[dayIndex].GetInt32();
                days.Add(new WeatherDayForecast(
                    date,
                    today ? current.GetProperty("weather_code").GetInt32() : dCodes[dayIndex].GetInt32(),
                    today ? current.GetProperty("temperature_2m").GetDouble() : dTemps[dayIndex].GetDouble(),
                    today ? current.GetProperty("apparent_temperature").GetDouble() : dApparent[dayIndex].GetDouble(),
                    today ? current.GetProperty("relative_humidity_2m").GetInt32() : representativeHumidity,
                    today ? current.GetProperty("wind_speed_10m").GetDouble() : dWind[dayIndex].GetDouble(),
                    rain, FormatTime(dSunrise[dayIndex].GetString()), FormatTime(dSunset[dayIndex].GetString()), hours));
            }

            Dispatcher.Invoke(() =>
            {
                if (generation != _requestGeneration) return;
                _forecastDays.Clear();
                _forecastDays.AddRange(days);
                _selectedDayIndex = 0;
                _hourPageStart = 0;
                _hasCurrentWeather = days.Count > 0;
                txtCitySearch.Text = displayName;
                ShowSelectedDay();
            });
        }

        private void ShowSelectedDay()
        {
            if (_forecastDays.Count == 0) return;
            _selectedDayIndex = Math.Clamp(_selectedDayIndex, 0, _forecastDays.Count - 1);
            WeatherDayForecast day = _forecastDays[_selectedDayIndex];
            _currentWeatherCode = day.WeatherCode;
            _currentApparentTemperature = day.ApparentTemperature;_currentTemperature=day.Temperature;
            txtCurrentTemp.Text = TemperatureText(day.Temperature);
            txtCondition.Text = GetWeatherDescription(day.WeatherCode);
            txtFeelsLike.Text = TemperatureText(day.ApparentTemperature);
            txtHumidity.Text = $"{day.Humidity}%";
            txtWind.Text = $"{Math.Round(day.WindSpeed)} {Translate("km/h")}";
            txtRainChance.Text = $"{day.RainChance}%";
            txtSunrise.Text = day.Sunrise;
            txtSunset.Text = day.Sunset;
            currentWeatherIcon.WeatherCode = day.WeatherCode;
            txtSelectedDate.Text = FormatForecastDate(day.Date,DisplayLanguage);
            // This badge sizes to its text; the shared optical control invalidates
            // paint only, so an asynchronously loaded date also needs measuring.
            txtSelectedDate.InvalidateMeasure();
            // Keep the automatic return timer active without showing its countdown badge.
            returnCountdownBadge.Visibility = Visibility.Collapsed;
            if (_selectedDayIndex == 0) _returnTimer.Stop();
            previousDayButton.IsEnabled = _selectedDayIndex > 0;
            nextDayButton.IsEnabled = _selectedDayIndex < _forecastDays.Count - 1;
            _hourPageStart = Math.Clamp(_hourPageStart, 0, Math.Max(0, day.Hours.Count - HourlyPageSize));
            hourlyForecast.ItemsSource = day.Hours.Skip(_hourPageStart).Take(HourlyPageSize).Select(h=>h.RawTemperature is double temperature?h with{Temperature=TemperatureText(temperature)}:h).ToArray();
            previousHoursButton.IsEnabled = _hourPageStart > 0;
            nextHoursButton.IsEnabled = _hourPageStart + HourlyPageSize < day.Hours.Count;
        }

        private void ChangeDay(int delta)
        {
            if (_forecastDays.Count == 0) return;
            int next = Math.Clamp(_selectedDayIndex + delta, 0, _forecastDays.Count - 1);
            if (next == _selectedDayIndex) return;
            StartReturnCountdown();
            AnimatePageChange(() => { _selectedDayIndex = next; _hourPageStart = 0; ShowSelectedDay(); }, delta);
        }

        private void ChangeHours(int delta)
        {
            if (_forecastDays.Count == 0) return;
            int max = Math.Max(0, _forecastDays[_selectedDayIndex].Hours.Count - HourlyPageSize);
            int next = Math.Clamp(_hourPageStart + delta, 0, max);
            if (next == _hourPageStart) return;
            if (_selectedDayIndex != 0) StartReturnCountdown();
            AnimateHourlyChange(() => { _hourPageStart = next; ShowSelectedDay(); }, delta);
        }

        private void PreviousDayButton_Click(object sender, System.Windows.RoutedEventArgs e) => ChangeDay(-1);
        private void NextDayButton_Click(object sender, System.Windows.RoutedEventArgs e) => ChangeDay(1);
        private void PreviousHoursButton_Click(object sender, System.Windows.RoutedEventArgs e) => ChangeHours(-HourlyPageSize);
        private void NextHoursButton_Click(object sender, System.Windows.RoutedEventArgs e) => ChangeHours(HourlyPageSize);

        private void StartReturnCountdown()
        {
            _returnSeconds = 45;
            UpdateReturnCountdown();
            _returnTimer.Start();
        }

        private void UpdateReturnCountdown() => returnCountdownText.Text = $"{Translate("TODAY IN")} {_returnSeconds}{Translate("s")}";

        private void AnimatePageChange(Action change, int direction)
        {
            if (_pageTransitionRunning) return;
            _pageTransitionRunning = true;
            AnimateElements(new FrameworkElement[] { daySummaryContent, dayMetricsContent }, change, direction,
                () => _pageTransitionRunning = false);
        }

        private void AnimateHourlyChange(Action change, int direction) =>
            AnimateElements(new FrameworkElement[] { hourlyForecast }, change, direction, null);

        private static void AnimateElements(FrameworkElement[] elements, Action change, int direction, Action? completed)
        {
            foreach (FrameworkElement element in elements)
            {
                element.RenderTransform = new TranslateTransform();
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90)));
            }
            DispatcherTimer delay = new() { Interval = TimeSpan.FromMilliseconds(95) };
            delay.Tick += (_, _) =>
            {
                delay.Stop(); change();
                foreach (FrameworkElement element in elements)
                {
                    element.Opacity = 0;
                    TranslateTransform transform = (TranslateTransform)element.RenderTransform;
                    transform.X = direction > 0 ? 18 : -18;
                    element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                    transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(transform.X, 0, TimeSpan.FromMilliseconds(220))
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                }
                completed?.Invoke();
            };
            delay.Start();
        }

        private void Weather_PreviewTouchDown(object? sender, TouchEventArgs e)
        {
            Point widgetPoint = e.GetTouchPoint(this).Position;
            _activeSwipeZone = IsPointInHourlySwipeZone(widgetPoint)
                ? SwipeZone.Hourly
                : IsPointInside(daySwipeArea, widgetPoint)
                    ? SwipeZone.Day
                    : SwipeZone.None;

            if (_activeSwipeZone == SwipeZone.None)
                return;

            Point localPoint = e.GetTouchPoint(
                _activeSwipeZone == SwipeZone.Hourly ? hourlySwipeArea : daySwipeArea).Position;
            _dayTouchStart = localPoint;
            _hourTouchStart = localPoint;
            _daySwipeHandled = false;
            _hourSwipeHandled = false;
            CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void Weather_PreviewTouchMove(object? sender, TouchEventArgs e)
        {
            if (_activeSwipeZone == SwipeZone.None)
                return;

            FrameworkElement area = _activeSwipeZone == SwipeZone.Hourly ? hourlySwipeArea : daySwipeArea;
            Point start = _activeSwipeZone == SwipeZone.Hourly ? _hourTouchStart : _dayTouchStart;
            Point current = e.GetTouchPoint(area).Position;
            double deltaX = current.X - start.X;
            double deltaY = current.Y - start.Y;
            bool alreadyHandled = _activeSwipeZone == SwipeZone.Hourly ? _hourSwipeHandled : _daySwipeHandled;
            if (!alreadyHandled && Math.Abs(deltaX) >= 10 && Math.Abs(deltaX) >= Math.Abs(deltaY) * 0.7)
            {
                if (_activeSwipeZone == SwipeZone.Hourly)
                {
                    _hourSwipeHandled = true;
                    ChangeHours(deltaX < 0 ? HourlyPageSize : -HourlyPageSize);
                }
                else
                {
                    _daySwipeHandled = true;
                    ChangeDay(deltaX < 0 ? 1 : -1);
                }
            }
            e.Handled = true;
        }

        private void Weather_PreviewTouchUp(object? sender, TouchEventArgs e)
        {
            if (_activeSwipeZone == SwipeZone.None)
                return;

            FrameworkElement area = _activeSwipeZone == SwipeZone.Hourly ? hourlySwipeArea : daySwipeArea;
            Point start = _activeSwipeZone == SwipeZone.Hourly ? _hourTouchStart : _dayTouchStart;
            double delta = e.GetTouchPoint(area).Position.X - start.X;
            bool alreadyHandled = _activeSwipeZone == SwipeZone.Hourly ? _hourSwipeHandled : _daySwipeHandled;
            if (!alreadyHandled && Math.Abs(delta) >= 10)
            {
                if (_activeSwipeZone == SwipeZone.Hourly)
                    ChangeHours(delta < 0 ? HourlyPageSize : -HourlyPageSize);
                else ChangeDay(delta < 0 ? 1 : -1);
            }

            ReleaseTouchCapture(e.TouchDevice);
            _activeSwipeZone = SwipeZone.None;
            _daySwipeHandled = false;
            _hourSwipeHandled = false;
            e.Handled = true;
        }

        private bool IsPointInside(FrameworkElement element, Point widgetPoint)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;
            Point origin = element.TranslatePoint(new Point(0, 0), this);
            return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)).Contains(widgetPoint);
        }

        private bool IsPointInHourlySwipeZone(Point widgetPoint)
        {
            if (!hourlySwipeArea.IsVisible ||
                hourlySwipeArea.ActualWidth <= 0 ||
                ActualHeight <= 0)
                return false;

            Point origin = hourlySwipeArea.TranslatePoint(new Point(0, 0), this);
            return widgetPoint.X >= origin.X &&
                   widgetPoint.X <= origin.X + hourlySwipeArea.ActualWidth &&
                   widgetPoint.Y >= origin.Y &&
                   widgetPoint.Y <= ActualHeight;
        }

        private enum SwipeZone { None, Day, Hourly }

        private string GetCurrentSourceKey()
        {
            if (_coordinateOverride is { } coordinates)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"coordinates:{coordinates.Latitude:R},{coordinates.Longitude:R}");
            }

            return $"city:{_cityQuery.Trim().ToUpperInvariant()}";
        }

        private WeatherCache? LoadCache()
        {
            try
            {
                if (!File.Exists(_cacheFile))
                    return null;

                return JsonSerializer.Deserialize<WeatherCache>(
                    File.ReadAllText(_cacheFile));
            }
            catch
            {
                return null;
            }
        }

        private void SaveCache(WeatherCache cache)
        {
            try
            {
                string? folder = Path.GetDirectoryName(_cacheFile);
                if (folder != null)
                    Directory.CreateDirectory(folder);

                File.WriteAllText(
                    _cacheFile,
                    JsonSerializer.Serialize(cache));
            }
            catch
            {
                // Neišsaugota talpykla neturi stabdyti gyvų orų atnaujinimo.
            }
        }

        private static bool TryParseCoordinates(
            string? text,
            out double latitude,
            out double longitude)
        {
            latitude = 0;
            longitude = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] parts = text.Split(',', 2, StringSplitOptions.TrimEntries);

            return parts.Length == 2 &&
                   double.TryParse(
                       parts[0],
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out latitude) &&
                   double.TryParse(
                       parts[1],
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out longitude) &&
                   latitude is >= -90 and <= 90 &&
                   longitude is >= -180 and <= 180;
        }

        private static string FormatTime(string? value)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                out DateTime time)
                ? time.ToString("HH:mm")
                : "--:--";
        }

        private void LanguageManager_Changed(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(ApplyLanguage);
        }

        internal string DisplayLanguage {get;private set;}="en";
        internal string Translate(string english)=>_translations.Text(DisplayLanguage,english);
        private void SetConditionStatus(string english){_conditionTextKey=english;txtCondition.Text=Translate(english);}
        internal void SetDisplayLanguage(string language)
        {
            try{DisplayLanguage=WeatherTranslationStore.NormalizeCode(language);}catch(ArgumentException){DisplayLanguage="en";}
            _translations.Reload();ApplyLanguage();ShowSelectedDay();UpdateConditionWidth();UpdateReturnCountdown();DisplayLanguageChanged?.Invoke(this,EventArgs.Empty);
        }
        private void UpdateConditionWidth()
        {
            // Reserve a two-digit signed decimal reading at the actual font size.
            // The illustration receives the remaining width; readings never shrink.
            var reference=new FormattedText(FormatTemperature(-99.9,ShowTemperatureDecimals,DisplayLanguage),
                CultureInfo.GetCultureInfo(DisplayLanguage),txtCurrentTemp.FlowDirection,
                new Typeface(txtCurrentTemp.FontFamily,txtCurrentTemp.FontStyle,txtCurrentTemp.FontWeight,txtCurrentTemp.FontStretch),
                txtCurrentTemp.FontSize,txtCurrentTemp.Foreground,VisualTreeHelper.GetDpi(txtCurrentTemp).PixelsPerDip);
            double width=Math.Ceiling(reference.WidthIncludingTrailingWhitespace)+txtCurrentTemp.Padding.Right+2;
            if(weatherSummaryTextStack.Width!=width)weatherSummaryTextStack.Width=width;
            conditionViewbox.MaxWidth=Math.Max(0,width-conditionViewbox.Margin.Left-conditionViewbox.Margin.Right);
        }
        private void ApplyLanguage()
        {
            txtWeatherHeading.Text = Translate("WEATHER");
            previousDayButton.ToolTip=Translate("Previous day");nextDayButton.ToolTip=Translate("Next day");
            previousHoursButton.ToolTip=Translate("Previous hours");nextHoursButton.ToolTip=Translate("Next hours");
            txtFeelsLike.ToolTip=Translate("Feels like");txtWind.ToolTip=Translate("Wind");txtHumidity.ToolTip=Translate("Humidity");
            txtRainChance.ToolTip=Translate("Chance of rain");txtSunrise.ToolTip=Translate("Sunrise");txtSunset.ToolTip=Translate("Sunset");
            if(!_locationEnabled)txtCitySearch.Text=Translate("Select place");

            if (_hasCurrentWeather)
            {
                txtCondition.Text = GetWeatherDescription(_currentWeatherCode);
                txtFeelsLike.Text =
                    TemperatureText(_currentApparentTemperature);
            }
            else
            {
                txtCondition.Text = Translate(_conditionTextKey);
                txtWind.Text="-- "+Translate("km/h");
                txtFeelsLike.Text = "--°";
            }
        }

        private string GetWeatherDescription(int code)
        {
            return Translate(code switch
                {
                    0 => "Clear",
                    1 => "Mostly clear",
                    2 => "Partly cloudy",
                    3 => "Cloudy",
                    45 or 48 => "Fog",
                    51 or 53 or 55 => "Drizzle",
                    61 or 63 or 65 => "Rain",
                    71 or 73 or 75 or 77 => "Snow",
                    80 or 81 or 82 => "Rain showers",
                    95 or 96 or 99 => "Thunderstorm",
                    _ => "Variable"
                });
        }

        private sealed record WeatherCache(
            string SourceKey,
            string DisplayName,
            string WeatherJson,
            DateTime SavedAtUtc);

        private sealed record HourlyForecastItem(
            string Time,
            int WeatherCode,
            string Temperature, double? RawTemperature=null);

        private sealed record WeatherDayForecast(
            DateTime Date,
            int WeatherCode,
            double Temperature,
            double ApparentTemperature,
            int Humidity,
            double WindSpeed,
            int RainChance,
            string Sunrise,
            string Sunset,
            List<HourlyForecastItem> Hours);
    }
}
