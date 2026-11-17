using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ThinkingSpace.Pairing.Editor
{
    public static class AndroidCredentialVaultTestPlayerBuilder
    {
        private const string OutputVariable = "THINKING_SPACE_VAULT_TEST_APK_OUTPUT";
        private const string TestName =
            "ThinkingSpace.Tests.PlayMode.AndroidQuestCredentialVaultTests.DedicatedAliasEncryptsRotatesPersistsRejectsCorruptionAndDeletes";
        private static DateTime deadlineUtc;
        private static string outputPath;
        private static bool observedFailure;

        public static void BuildOnlyFromCommandLine()
        {
            outputPath = Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathRooted(outputPath)
                || !string.Equals(Path.GetExtension(outputPath), ".apk", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(OutputVariable + " must be an absolute APK path.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            if (File.Exists(outputPath)) File.Delete(outputPath);

            Type settingsType = Type.GetType(
                "UnityEditor.TestTools.TestRunner.PlayerLauncherTestRunSettings, UnityEditor.TestRunner", true);
            object playerSettings = Activator.CreateInstance(settingsType, true);
            settingsType.GetProperty("buildOnly").SetValue(playerSettings, true);
            settingsType.GetProperty("buildOnlyLocationPath").SetValue(playerSettings, outputPath);
            var execution = new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, testNames = new[] { TestName } })
            { overloadTestRunSettings = (ITestRunSettings)playerSettings };
            typeof(ExecutionSettings).GetProperty("targetPlatform", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(execution, (BuildTarget?)BuildTarget.Android);

            observedFailure = false;
            deadlineUtc = DateTime.UtcNow.AddMinutes(20);
            Application.logMessageReceived += ObserveLog;
            EditorApplication.update += WaitForBuild;
            ScriptableObject.CreateInstance<TestRunnerApi>().Execute(execution);
        }

        private static void WaitForBuild()
        {
            if (File.Exists(outputPath) && !BuildPipeline.isBuildingPlayer) { Finish(0); return; }
            if (observedFailure || DateTime.UtcNow >= deadlineUtc) Finish(1);
        }

        private static void ObserveLog(string condition, string stackTrace, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception)
                && (condition.IndexOf("Player build failed", StringComparison.OrdinalIgnoreCase) >= 0
                    || condition.IndexOf("BuildFailedException", StringComparison.OrdinalIgnoreCase) >= 0))
                observedFailure = true;
        }

        private static void Finish(int exitCode)
        {
            Application.logMessageReceived -= ObserveLog;
            EditorApplication.update -= WaitForBuild;
            Debug.Log(exitCode == 0 ? "Credential-vault test-player build completed."
                : "Credential-vault test-player build failed.");
            EditorApplication.Exit(exitCode);
        }
    }
}
