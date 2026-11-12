using System.IO;
using System.Linq;
using ThinkingSpace.UI;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace ThinkingSpace.Editor
{
    public static class ThinkingSpaceLocalizationAssetGenerator
    {
        private const string Root = "Assets/ThinkingSpace/Localization";
        private const string LocaleRoot = Root + "/Locales";
        private const string TableRoot = Root + "/String Tables";

        [MenuItem("Thinking Space/Localization/Generate Foundation Assets")]
        public static void Generate()
        {
            EnsureFolder("Assets/ThinkingSpace", "Localization");
            EnsureFolder(Root, "Locales");
            EnsureFolder(Root, "String Tables");

            Locale english=EnsureLocale("en");
            Locale spanish=EnsureLocale("es");
            Locale french=EnsureLocale("fr");
            Locale mandarin=EnsureLocale("zh-CN");
            StringTableCollection collection=FindCollection();
            if(collection==null)
                collection=LocalizationEditorSettings.CreateStringTableCollection(
                    ThinkingSpaceStringCatalog.TableName,TableRoot);

            Populate(collection,english,"en");
            Populate(collection,spanish,"es");
            Populate(collection,french,"fr");
            Populate(collection,mandarin,"zh-CN");
            EnsureChineseFont();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Generated Thinking Space localization tables for en, es, fr and prepared-only zh-CN. hi-IN remains registration-only.");
        }

        public static void BuildDevelopmentApk()
        {
            string output=System.Environment.GetEnvironmentVariable("THINKING_SPACE_APK_OUTPUT");
            if(string.IsNullOrWhiteSpace(output))
                throw new System.InvalidOperationException("THINKING_SPACE_APK_OUTPUT must name the Development APK destination.");
            string[] scenes=System.Array.FindAll(EditorBuildSettings.scenes,scene=>scene.enabled)
                .Select(scene=>scene.path).ToArray();
            if(scenes.Length==0)throw new System.InvalidOperationException("No enabled scenes are configured for the build.");
            string directory=Path.GetDirectoryName(output);
            if(!string.IsNullOrEmpty(directory))Directory.CreateDirectory(directory);
            bool previousBundle=EditorUserBuildSettings.buildAppBundle;
            bool previousDevelopment=EditorUserBuildSettings.development;
            bool previousDebugging=EditorUserBuildSettings.allowDebugging;
            try
            {
                EditorUserBuildSettings.buildAppBundle=false;
                EditorUserBuildSettings.development=true;
                EditorUserBuildSettings.allowDebugging=true;
                BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes=scenes,
                    locationPathName=output,
                    target=BuildTarget.Android,
                    options=BuildOptions.Development|BuildOptions.AllowDebugging
                });
                if(report.summary.result!=BuildResult.Succeeded)
                    throw new System.InvalidOperationException($"Development APK build failed: {report.summary.result}");
                Debug.Log($"Thinking Space Development APK created ({report.summary.totalSize} bytes). Location withheld from runtime logs.");
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle=previousBundle;
                EditorUserBuildSettings.development=previousDevelopment;
                EditorUserBuildSettings.allowDebugging=previousDebugging;
            }
        }

        private static Locale EnsureLocale(string code)
        {
            Locale existing=LocalizationEditorSettings.GetLocale(code);
            if(existing!=null)return existing;
            Locale locale=Locale.CreateLocale(code);
            string path=$"{LocaleRoot}/{code}.asset";
            AssetDatabase.CreateAsset(locale,path);
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        private static StringTableCollection FindCollection()
        {
            foreach(StringTableCollection collection in LocalizationEditorSettings.GetStringTableCollections())
                if(collection.TableCollectionName==ThinkingSpaceStringCatalog.TableName)return collection;
            return null;
        }

        private static void Populate(StringTableCollection collection,Locale locale,string code)
        {
            StringTable table=collection.GetTable(locale.Identifier) as StringTable;
            if(table==null)table=collection.AddNewTable(locale.Identifier) as StringTable;
            foreach(ThinkingSpaceStringEntry source in ThinkingSpaceStringCatalog.Entries)
            {
                string value=ThinkingSpaceLocalizationController.TranslateForLanguage(code,source.Key);
                StringTableEntry entry=table.GetEntry(source.Key)??table.AddEntry(source.Key,value);
                entry.Value=value;
            }
            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(collection.SharedData);
        }

        private static void EnsureChineseFont()
        {
            const string sourcePath="Assets/ThinkingSpace/UI/Fonts/NotoSansSC/NotoSansCJKsc-Regular.otf";
            const string assetPath="Assets/ThinkingSpace/UI/Fonts/NotoSansSC/NotoSansCJKsc-Regular SDF.asset";
            TMPro.TMP_FontAsset existing=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(assetPath);
            if(existing!=null&&existing.atlasTextures!=null&&existing.atlasTextures.Length>0
                &&existing.atlasTextures[0]!=null&&existing.material!=null)
            {RegisterFallback(existing);return;}
            if(existing!=null)AssetDatabase.DeleteAsset(assetPath);
            Font source=AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if(source==null){Debug.LogWarning("Noto Sans CJK SC source font is missing.");return;}
            TMPro.TMP_FontAsset font=TMPro.TMP_FontAsset.CreateFontAsset(source);
            font.atlasPopulationMode=TMPro.AtlasPopulationMode.Dynamic;
            font.name="NotoSansCJKsc-Regular SDF";
            AssetDatabase.CreateAsset(font,assetPath);
            if(font.material!=null)AssetDatabase.AddObjectToAsset(font.material,font);
            if(font.atlasTextures!=null)
                foreach(Texture2D atlas in font.atlasTextures)
                    if(atlas!=null)AssetDatabase.AddObjectToAsset(atlas,font);
            EditorUtility.SetDirty(font);
            RegisterFallback(font);
        }

        private static void RegisterFallback(TMPro.TMP_FontAsset font)
        {
            TMPro.TMP_FontAsset root=TMPro.TMP_Settings.defaultFontAsset;
            if(root==null||font==null||root==font)return;
            root.fallbackFontAssetTable??=new System.Collections.Generic.List<TMPro.TMP_FontAsset>();
            if(!root.fallbackFontAssetTable.Contains(font))root.fallbackFontAssetTable.Add(font);
            EditorUtility.SetDirty(root);
        }

        private static void EnsureFolder(string parent,string child)
        {
            string path=Path.Combine(parent,child).Replace('\\','/');
            if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(parent,child);
        }
    }
}
