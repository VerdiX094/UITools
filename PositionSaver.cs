using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SFS.IO;
using SFS.Parsers.Json;
using SFS.UI.ModGUI;
using UnityEngine;

namespace UITools
{
    /// <summary>
    ///     Utility class that adds extra saving functionality
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public static class PositionSaver
    {
        private const int AUTOSAVE_SECONDS = 60;
        
        private static Dictionary<string, Vector2> windows = new(); // Apparently the indexes of this were failing to compile without the field being pre-initialized?
        private static FilePath saveFile;
        
        private static bool hasUnsavedChanges;
        private static bool quitting;
        
        static async void AutosaveLoop() { // probably a coroutine or InvokeRepeating would have been better here, but this ain't no monobehaviour
            while (!quitting)
            {
                await Task.Delay(AUTOSAVE_SECONDS * 1000);

                if (hasUnsavedChanges)
                    Save();
                hasUnsavedChanges = false;
            }
        }
        
        internal static void Initialize()
        {
            saveFile = new FolderPath(Main.main.ModFolder).ExtendToFile("positions.txt");

            AutosaveLoop();
            
            Load();
            Save();
            
            Application.quitting += () =>
            {
                quitting = true;
                Save();
            };
        }

        /// <summary>
        ///     Allow you to register you window for saving that will save position even through game relaunch.
        ///     You should call it every time you rebuild the window.
        ///     Default saving function should be disabled!
        /// </summary>
        /// <param name="window">Window that will be saved</param>
        /// <param name="uniqueName">Unique name id which uses to find your window position</param>
        /// <example>
        ///     The following code register window for permanent position saving
        ///     <code>
        /// Window window = Builder.CreateWindow(..., savePosition: false);
        /// window.RegisterPermanentSaving("UITools.myAwesomeWindow");
        /// </code>
        /// </example>
        public static void RegisterPermanentSaving(this Window window, string uniqueName)
        {
            if (windows.TryGetValue(uniqueName, out var savedPosition))
                window.Position = savedPosition;
            else
                windows.Add(uniqueName, window.Position);
            window.RegisterOnDropListener(() => OnPositionChange(uniqueName, window.Position));
        }

        static void OnPositionChange(string name, Vector2 position)
        {
            windows[name] = position;
            hasUnsavedChanges = true;
        }

        static void Load()
        {
            if (saveFile.FileExists())
                windows = JsonWrapper.FromJson<Dictionary<string, Vector2>>(saveFile.ReadText()) ?? windows; // Assign `windows` to itself if json deserialization failed
        }

        static void Save()
        {
            saveFile.WriteText(JsonWrapper.ToJson(windows, true));
        }
    }
}