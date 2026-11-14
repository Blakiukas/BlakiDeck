using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ekrano_valdymas;

internal sealed record WeatherTranslationEntry(string English,string Lithuanian,string Group);
internal sealed class WeatherLanguageDictionary
{
    public WeatherLanguageDictionary(){}
    public int Version {get;set;}=1;
    public string Code {get;set;}="";
    public string Name {get;set;}="";
    public Dictionary<string,string> Translations {get;set;}=new(StringComparer.Ordinal);
}
internal sealed class WeatherTranslationStore
{
    private sealed class State {public State(){}public int Version {get;set;}=1;public string LastTranslationCode {get;set;}="lt";public string[]? CustomLanguageCodes {get;set;}public List<WeatherLanguageDictionary> Languages {get;set;}=new();}
    internal const int MaximumCustomLanguages=3;
    private readonly string path;
    private Dictionary<string,WeatherLanguageDictionary> languages=new(StringComparer.Ordinal);
    private static IReadOnlyList<WeatherTranslationEntry> LegacyEntries {get;}=CreateEntries();
    internal static IReadOnlyList<WeatherTranslationEntry> Entries {get;}=LegacyEntries.TakeWhile(entry=>entry.Group!="Places").ToArray();
    internal string LastTranslationCode {get;private set;}="lt";
    internal IReadOnlyList<string> CustomLanguageCodes {get;private set;}=Array.Empty<string>();
    internal IReadOnlyList<WeatherLanguageDictionary> SelectableLanguages=>new[]{LoadLanguage("en"),LoadLanguage("lt")}.Concat(CustomLanguageCodes.Select(LoadLanguage)).ToArray();
    internal WeatherTranslationStore(string? file=null){path=file??Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","weather-translations-v1.json");Reload();}
    internal static string NormalizeCode(string? code)
    {
        string value=(code??"").Trim().ToLowerInvariant();
        if(value.Length>35||!Regex.IsMatch(value,"^[a-z]{2,8}(?:-[a-z0-9]{1,8})*$",RegexOptions.CultureInvariant))throw new ArgumentException("Use a language code such as lt, ru or de-DE.");
        return value;
    }
    internal void Reload()
    {
        State? state=null;
        foreach(string file in new[]{path,path+".previous"})try{if(File.Exists(file)&&new FileInfo(file).Length<=1_000_000){state=JsonSerializer.Deserialize<State>(File.ReadAllText(file));if(state?.Version==1&&state.Languages!=null)break;state=null;}}catch{state=null;}
        var loaded=new Dictionary<string,WeatherLanguageDictionary>(StringComparer.Ordinal);
        foreach(var value in state?.Languages??new())try{var valid=Validate(value);if(valid.Code!="en")loaded[valid.Code]=valid;}catch{/* A damaged entry must not hide built-in English/Lithuanian. */}
        languages=loaded;
        try{LastTranslationCode=NormalizeCode(state?.LastTranslationCode??"lt");if(LastTranslationCode=="en")LastTranslationCode="lt";}catch(ArgumentException){LastTranslationCode="lt";}
        var codes=new List<string>();
        void AddCode(string? input){try{string code=NormalizeCode(input);if(code is not "en" and not "lt"&&!codes.Contains(code,StringComparer.Ordinal))codes.Add(code);}catch(ArgumentException){}}
        if(state?.CustomLanguageCodes is {} savedCodes)foreach(string code in savedCodes)AddCode(code);
        else
        {
            // Earlier versions exposed only the last custom code. Keep that
            // selection first and retain every dictionary, even beyond3slots.
            AddCode(LastTranslationCode);
            foreach(string code in loaded.Keys.Order(StringComparer.Ordinal))AddCode(code);
        }
        CustomLanguageCodes=codes.Take(MaximumCustomLanguages).ToArray();
    }
    internal IReadOnlyList<WeatherLanguageDictionary> Languages=>new[]{LoadLanguage("en"),LoadLanguage("lt")}.Concat(languages.Values.Where(v=>v.Code!="lt").OrderBy(v=>v.Name,StringComparer.CurrentCultureIgnoreCase).Select(Clone)).ToArray();
    internal WeatherLanguageDictionary LoadLanguage(string code)
    {
        code=NormalizeCode(code);if(languages.TryGetValue(code,out var saved))return Clone(saved);
        var entries=code is "en" or "lt"?LegacyEntries:Entries;
        return new(){Code=code,Name=code=="en"?"English":code=="lt"?"Lietuvių":code.ToUpperInvariant(),Translations=entries.ToDictionary(e=>e.English,e=>code=="lt"?e.Lithuanian:code=="en"?e.English:"",StringComparer.Ordinal)};
    }
    internal string Text(string code,string english)
    {
        if(code=="en")return english;
        if(languages.TryGetValue(code,out var saved))return saved.Translations.TryGetValue(english,out string? value)&&!string.IsNullOrWhiteSpace(value)?value:english;
        return code=="lt"?LegacyEntries.FirstOrDefault(e=>e.English==english)?.Lithuanian??english:english;
    }
    internal void SaveLanguage(WeatherLanguageDictionary value)
    {
        value=Validate(value);if(value.Code=="en")throw new ArgumentException("English originals are read-only. Choose another language code.");
        // Reload first so editing one language preserves every other dictionary.
        Reload();var next=languages.ToDictionary(p=>p.Key,p=>Clone(p.Value),StringComparer.Ordinal);
        // Hidden legacy fields remain round-trippable, but are no longer rows
        // in the weather editor. Editing visible weather never erases them.
        var previous=LoadLanguage(value.Code);var visible=Entries.Select(entry=>entry.English).ToHashSet(StringComparer.Ordinal);
        foreach(var pair in previous.Translations)if(!visible.Contains(pair.Key)&&!value.Translations.ContainsKey(pair.Key))value.Translations[pair.Key]=pair.Value;
        var codes=CustomLanguageCodes.ToList();
        if(value.Code is not "lt"&&!codes.Contains(value.Code,StringComparer.Ordinal))
        {
            if(codes.Count>=MaximumCustomLanguages)throw new ArgumentException("Up to three additional languages can be shown. Remove one from the choices first; its saved translations will be kept.");
            codes.Add(value.Code);
        }
        next[value.Code]=value;WriteState(next,codes,value.Code);
    }
    internal void RememberTranslationLanguage(string code)
    {
        code=NormalizeCode(code);if(code=="en")return;Reload();
        var codes=CustomLanguageCodes.ToList();
        if(code!="lt"&&!codes.Contains(code,StringComparer.Ordinal))
        {
            // Recover a legacy selected code without dropping its dictionary.
            // This also handles a settings file restored independently of slots.
            codes.Insert(0,code);codes=codes.Take(MaximumCustomLanguages).ToList();
        }
        if(LastTranslationCode==code&&codes.SequenceEqual(CustomLanguageCodes))return;
        WriteState(languages,codes,code);
    }
    internal bool RemoveCustomLanguage(string code)
    {
        code=NormalizeCode(code);Reload();if(code is "en" or "lt"||!CustomLanguageCodes.Contains(code,StringComparer.Ordinal))return false;
        WriteState(languages,CustomLanguageCodes.Where(saved=>saved!=code).ToArray(),LastTranslationCode==code?"lt":LastTranslationCode);return true;
    }
    private void WriteState(Dictionary<string,WeatherLanguageDictionary> next,IReadOnlyList<string> codes,string last)
    {
        AtomicJson.Write(path,new State{LastTranslationCode=last,CustomLanguageCodes=codes.ToArray(),Languages=next.Values.OrderBy(value=>value.Code,StringComparer.Ordinal).Select(Clone).ToList()});
        languages=next;CustomLanguageCodes=codes.ToArray();LastTranslationCode=last;
    }
    internal static WeatherLanguageDictionary Import(string json)
    {
        if(json.Length>1_000_000)throw new ArgumentException("The translation file is too large.");
        return Validate(JsonSerializer.Deserialize<WeatherLanguageDictionary>(json)??throw new ArgumentException("The translation file is empty."));
    }
    internal static string Export(WeatherLanguageDictionary value)=>JsonSerializer.Serialize(Validate(value),new JsonSerializerOptions{WriteIndented=true});
    private static WeatherLanguageDictionary Validate(WeatherLanguageDictionary value)
    {
        if(value.Version!=1)throw new ArgumentException("Unsupported translation file version.");
        string code=NormalizeCode(value.Code),name=(value.Name??"").Trim();
        if(name.Length==0||name.Length>80)throw new ArgumentException("Enter a language name (up to 80 characters).");
        if(value.Translations==null)throw new ArgumentException("The translation dictionary is missing.");
        var known=LegacyEntries.Select(e=>e.English).ToHashSet(StringComparer.Ordinal);
        if(value.Translations.Any(p=>!known.Contains(p.Key)))throw new ArgumentException("The file contains an unknown English original.");
        if(value.Translations.Any(p=>(p.Value?.Length??0)>500))throw new ArgumentException("A translation can contain up to 500 characters.");
        var translations=Entries.ToDictionary(e=>e.English,e=>value.Translations.GetValueOrDefault(e.English)?.Trim()??"",StringComparer.Ordinal);
        foreach(var pair in value.Translations)if(!translations.ContainsKey(pair.Key))translations[pair.Key]=pair.Value?.Trim()??"";
        return new(){Code=code,Name=name,Translations=translations};
    }
    private static WeatherLanguageDictionary Clone(WeatherLanguageDictionary value)=>new(){Code=value.Code,Name=value.Name,Translations=new(value.Translations,StringComparer.Ordinal)};
    internal string FormatDate(DateTime date,string code)=>$"{Text(code,date.ToString("MMMM",CultureInfo.InvariantCulture))} {date.Day}, {Text(code,date.ToString("dddd",CultureInfo.InvariantCulture))}";
    private static IReadOnlyList<WeatherTranslationEntry> CreateEntries()
    {
        var entries=new List<WeatherTranslationEntry>();
        void Add(string group,params string[] pairs){for(int i=0;i<pairs.Length;i+=2)entries.Add(new(pairs[i],pairs[i+1],group));}
        Add("Weather","WEATHER","ORAI","Loading...","Kraunama...","Offline","Nėra ryšio","Not found","Nerasta","Select place","Pasirinkti vietą","Add a place in settings","Pridėkite vietą nustatymuose","TODAY IN","ŠIANDIEN PO","s","s","km/h","km/h",
            "Clear","Giedra","Mostly clear","Daugiausia giedra","Partly cloudy","Debesuota su pragiedruliais","Cloudy","Debesuota","Fog","Rūkas","Drizzle","Dulksna","Rain","Lietus","Snow","Sniegas","Rain showers","Liūtis","Thunderstorm","Perkūnija","Variable","Permaininga");
        Add("Navigation and metrics","Previous day","Ankstesnė diena","Next day","Kita diena","Previous hours","Ankstesnės valandos","Next hours","Kitos valandos","Feels like","Jaučiama temperatūra","Wind","Vėjas","Humidity","Drėgmė","Chance of rain","Lietaus tikimybė","Sunrise","Saulėtekis","Sunset","Saulėlydis");
        Add("Wind directions","North","Šiaurė","Northeast","Šiaurės rytai","East","Rytai","Southeast","Pietryčiai","South","Pietūs","Southwest","Pietvakariai","West","Vakarai","Northwest","Šiaurės vakarai");
        Add("Months","January","Sausio","February","Vasario","March","Kovo","April","Balandžio","May","Gegužės","June","Birželio","July","Liepos","August","Rugpjūčio","September","Rugsėjo","October","Spalio","November","Lapkričio","December","Gruodžio");
        Add("Days","Monday","pirmadienis","Tuesday","antradienis","Wednesday","trečiadienis","Thursday","ketvirtadienis","Friday","penktadienis","Saturday","šeštadienis","Sunday","sekmadienis");
        Add("Display","Temperature: one decimal place","Temperatūra: vienas skaičius po kablelio");
        Add("Places","Language","Kalba","Translations…","Vertimai…","Place names","Vietovardžiai","City","Miestas","Coordinates","Koordinatės","Latitude, longitude","Platuma, ilguma","Enter a city, then choose Add","Įveskite miestą ir pasirinkite Pridėti","Display name","Rodomas pavadinimas","Save","Išsaugoti","Add","Pridėti","SAVED PLACES","IŠSAUGOTOS VIETOS","Clear input","Išvalyti lauką","Delete saved place","Pašalinti išsaugotą vietą","Edited name","Redaguotas pavadinimas","Name from coordinates","Pavadinimas pagal koordinates","Automatically matched name","Automatiškai patikslintas pavadinimas",
            "Enter a display name.","Įveskite rodomą pavadinimą.","Finding place…","Ieškoma vietos…","Saved","Išsaugota","Could not find the place. Check coordinates or try Add again.","Vieta nerasta. Patikrinkite koordinates arba bandykite Pridėti dar kartą.","Could not find the place. Check your connection and try again.","Vieta nerasta. Patikrinkite ryšį ir bandykite dar kartą.","Use latitude, longitude (for example 59.668, 9.65).","Įveskite platumą, ilgumą (pvz., 59.668, 9.65).","Enter a city.","Įveskite miestą.","City not found. Try a more specific name.","Miestas nerastas. Patikslinkite pavadinimą.","No town found near these coordinates. Try another point.","Prie šių koordinačių gyvenvietė nerasta. Patikslinkite vietą.");
        Add("Translation editor","Weather translations","Orų vertimai","Language code","Kalbos kodas","Language name","Kalbos pavadinimas","Load","Įkelti","English original (read-only)","Angliškas originalas (tik skaityti)","Translation · blank uses English","Vertimas · tuščias laukas rodo angliškai","Import JSON…","Importuoti JSON…","Export JSON…","Eksportuoti JSON…","Save and use","Išsaugoti ir naudoti","Close","Uždaryti","Import weather translation","Importuoti orų vertimą","Export weather translation","Eksportuoti orų vertimą","Exported.","Eksportuota.",
            "Use a language code such as lt, ru or de-DE.","Įveskite kalbos kodą, pvz., lt, ru arba de-DE.","English originals are read-only. Choose another language code.","Angliški originalai skirti tik skaityti. Pasirinkite kitą kalbos kodą.","The translation file is too large.","Vertimo failas per didelis.","The translation file is empty.","Vertimo failas tuščias.","Unsupported translation file version.","Nepalaikoma vertimo failo versija.","Enter a language name (up to 80 characters).","Įveskite kalbos pavadinimą (iki 80 simbolių).","The translation dictionary is missing.","Trūksta vertimų žodyno.","The file contains an unknown English original.","Faile yra nežinomas angliškas originalas.","A translation can contain up to 500 characters.","Vertime gali būti iki 500 simbolių.","English is the original. Choose another code to save a translation.","Anglų kalba yra originalas. Vertimui išsaugoti pasirinkite kitą kodą.");
        return entries.AsReadOnly();
    }
}
