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
        
        private static Dictionary<string, Vector2> positions = new(); // Apparently the indexes of this were failing to compile without the field being pre-initialized?
        private static Dictionary<string, bool> minimizedStates = new();
        
        private static FilePath positionsFile;
        private static FilePath minimizedStatesFile;
        
        private static bool hasUnsavedChanges;
        private static bool quitting;
        
        static async Task AutosaveLoop() { // probably a coroutine or InvokeRepeating would have been better here, but this ain't no monobehaviour
            while (!quitting)
            {
                await Task.Delay(AUTOSAVE_SECONDS * 1000);

                if (hasUnsavedChanges)
                {
                    Save();
                    hasUnsavedChanges = false;
                }
            }
        }
        
        internal static void Initialize()
        {
            positionsFile = new FolderPath(Main.main.ModFolder).ExtendToFile("positions.txt");
            minimizedStatesFile = new FolderPath(Main.main.ModFolder).ExtendToFile("minimizedStates.txt");

            Task.Run(AutosaveLoop);
            
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
            if (window is ClosableWindow closable) // Backwards compatibility with old mods
            {
                RegisterPermanentSaving(closable, uniqueName);
                return;
            }
            
            if (positions.TryGetValue(uniqueName, out var savedPosition))
                window.Position = savedPosition;
            else
                positions.Add(uniqueName, window.Position);
            window.RegisterOnDropListener(() => OnPositionChange(uniqueName, window.Position));
        }
        
        /// <summary>
        ///     Allow you to register you window for saving that will save position and closed state even through game relaunch.
        ///     You should call it every time you rebuild the window.
        ///     Default saving function should be disabled!
        /// </summary>
        /// <param name="window">Window that will be saved</param>
        /// <param name="uniqueName">Unique name id which uses to find your window position</param>
        /// <example>
        ///     The following code register window for permanent position saving
        ///     <code>
        /// ClosableWindow window = UIToolsBuilder.CreateClosableWindow(..., savePosition: false);
        /// window.RegisterPermanentSaving("UITools.myAwesomeWindow");
        /// </code>
        /// </example>
        public static void RegisterPermanentSaving(this ClosableWindow window, string uniqueName)
        {
            if (positions.TryGetValue(uniqueName, out var savedPosition))
                window.Position = savedPosition;
            else
                positions.Add(uniqueName, window.Position);
            
            if (minimizedStates.TryGetValue(uniqueName, out var openState))
                window.Minimized = openState;
            else
                minimizedStates.Add(uniqueName, window.Minimized);
            
            window.RegisterOnDropListener(() => OnPositionChange(uniqueName, window.Position));
            window.OnMinimizedChangedEvent += () => OnMinimizedChange(uniqueName, window.Minimized);
        }

        static void OnPositionChange(string name, Vector2 position)
        {
            positions[name] = position;
            hasUnsavedChanges = true;
        }

        static void OnMinimizedChange(string name, bool newMinimizedState)
        {
            minimizedStates[name] = newMinimizedState;
            hasUnsavedChanges = true;
        }

        static void Load()
        {
            if (positionsFile.FileExists())
                positions = JsonWrapper.FromJson<Dictionary<string, Vector2>>(positionsFile.ReadText()) ?? positions; // Assign `positions` to itself if json deserialization failed
            if (minimizedStatesFile.FileExists())
                minimizedStates = JsonWrapper.FromJson<Dictionary<string, bool>>(minimizedStatesFile.ReadText()) ?? minimizedStates; // Same for `minimizedStates`
        }

        static void Save()
        {
            positionsFile.WriteText(JsonWrapper.ToJson(positions, true));
            minimizedStatesFile.WriteText(JsonWrapper.ToJson(minimizedStates, true));
        }
    }
}