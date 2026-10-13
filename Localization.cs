using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BurgerEmpire.Core
{
    // Presentation-only catalog. Save identifiers and simulation commands stay language neutral.
    public static partial class Localization
    {
        public static string Language {get;private set;}="tr";
        public static int Revision {get;private set;}
        public static CultureInfo Culture=>CultureInfo.GetCultureInfo(Language=="en"?"en-US":"tr-TR");
        static readonly Dictionary<string,string> cache=new Dictionary<string,string>();
        static readonly Dictionary<char,List<KeyValuePair<string,string>>> phrases;
        static Localization(){phrases=Build();}
        static readonly Regex percent=new Regex(@"%([0-9]+(?:[.,][0-9]+)?)");
        public static void SetLanguage(string code)
        {
            string next=code=="en"?"en":"tr";
            if(next==Language)return;
            Language=next;Revision++;cache.Clear();
        }
        static Dictionary<char,List<KeyValuePair<string,string>>> Build()
        {
            var all=new Dictionary<string,string>(StringComparer.Ordinal);var tr=CultureInfo.GetCultureInfo("tr-TR");
            foreach(var pair in Catalog)all[pair.Key]=pair.Value;
            foreach(var pair in Catalog) {
                string upper=pair.Key.ToUpper(tr),invariant=pair.Key.ToUpperInvariant();
                if(!all.ContainsKey(upper))all[upper]=pair.Value.ToUpperInvariant();
                if(!all.ContainsKey(invariant))all[invariant]=pair.Value.ToUpperInvariant();
            }
            return all.GroupBy(p=>p.Key[0]).ToDictionary(g=>g.Key,g=>g.OrderByDescending(p=>p.Key.Length).ToList());
        }
        public static string Text(string source)
        {
            if(Language!="en"||string.IsNullOrEmpty(source))return source??"";
            if(cache.TryGetValue(source,out var saved))return saved;
            // Longest explicit phrase wins. Only source text is scanned; translations are never rescanned.
            var result=new StringBuilder(source.Length);
            for(int i=0;i<source.Length;) {
                bool found=false;
                if(phrases.TryGetValue(source[i],out var candidates))foreach(var pair in candidates) {
                    int length=pair.Key.Length;
                    if(i+length>source.Length||string.CompareOrdinal(source,i,pair.Key,0,length)!=0)continue;
                    if(char.IsLetter(pair.Key[0])&&i>0&&char.IsLetter(source[i-1]))continue;
                    if(char.IsLetter(pair.Key[length-1])&&i+length<source.Length&&char.IsLetter(source[i+length]))continue;
                    result.Append(pair.Value);i+=length;found=true;break;
                }
                if(!found)result.Append(source[i++]);
            }
            saved=percent.Replace(result.ToString(),"$1%");
            if(cache.Count>=1024)cache.Clear();cache[source]=saved;return saved;
        }
    }
}
