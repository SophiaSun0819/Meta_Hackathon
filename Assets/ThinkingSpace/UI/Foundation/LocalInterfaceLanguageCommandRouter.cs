using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThinkingSpace.UI
{
    public enum LocalInterfaceLanguageCommandOutcome { NotCommand, Supported, Unsupported, Ambiguous }

    public readonly struct LocalInterfaceLanguageCommand
    {
        public readonly LocalInterfaceLanguageCommandOutcome Outcome;
        public readonly string LanguageCode;
        public bool IsRecognized=>Outcome!=LocalInterfaceLanguageCommandOutcome.NotCommand;
        public LocalInterfaceLanguageCommand(LocalInterfaceLanguageCommandOutcome outcome,string languageCode)
        {Outcome=outcome;LanguageCode=languageCode??string.Empty;}
    }

    public static class LocalInterfaceLanguageCommandRouter
    {
        private static readonly HashSet<string> Targets=new HashSet<string>
        {"ui","interface","menu","menus","panel","panels","interfaz","pantalla","pantallas","panneau","panneaux"};
        private static readonly HashSet<string> Actions=new HashSet<string>
        {"switch","change","set","put","translate","cambia","cambiar","pon","cambie","mettre","mettez","changer","changez","passer"};
        private static readonly Dictionary<string,string> Supported=new Dictionary<string,string>
        {
            {"english","en"},{"ingles","en"},{"anglais","en"},
            {"spanish","es"},{"espanol","es"},{"espagnol","es"},
            {"french","fr"},{"francais","fr"},{"frances","fr"}
        };
        private static readonly HashSet<string> KnownUnsupported=new HashSet<string>
        {"mandarin","chinese","hindi","german","italian","portuguese","arabic","japanese","korean"};

        public static LocalInterfaceLanguageCommand Parse(string transcript)
        {
            string normalized=Normalize(transcript);if(string.IsNullOrEmpty(normalized))return new LocalInterfaceLanguageCommand(LocalInterfaceLanguageCommandOutcome.NotCommand,null);
            string[] words=normalized.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            bool target=false,action=false;HashSet<string> languages=new HashSet<string>();bool unsupported=false;
            for(int i=0;i<words.Length;i++)
            {
                string word=words[i];target|=Targets.Contains(word);action|=Actions.Contains(word);
                if(Supported.TryGetValue(word,out string code))languages.Add(code);else unsupported|=KnownUnsupported.Contains(word);
            }
            if(!target||!action)return new LocalInterfaceLanguageCommand(LocalInterfaceLanguageCommandOutcome.NotCommand,null);
            if(languages.Count>1||(languages.Count==1&&unsupported))return new LocalInterfaceLanguageCommand(LocalInterfaceLanguageCommandOutcome.Ambiguous,null);
            if(languages.Count==1){foreach(string code in languages)return new LocalInterfaceLanguageCommand(LocalInterfaceLanguageCommandOutcome.Supported,code);}
            return new LocalInterfaceLanguageCommand(unsupported?LocalInterfaceLanguageCommandOutcome.Unsupported:LocalInterfaceLanguageCommandOutcome.Ambiguous,null);
        }

        private static string Normalize(string value)
        {
            string source=(value??string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder builder=new StringBuilder(source.Length);
            for(int i=0;i<source.Length;i++)if(CharUnicodeInfo.GetUnicodeCategory(source[i])!=UnicodeCategory.NonSpacingMark)builder.Append(source[i]);
            return Regex.Replace(builder.ToString(),"[^a-z0-9]+"," ").Trim();
        }
    }
}
