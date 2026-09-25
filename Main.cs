using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;
using ModLoader;
using UnityEngine;

namespace UITools
{
    /// <summary>
    ///     Main class of the mod
    /// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public class Main : Mod, IUpdatableMod
    {
        internal static Main main;

        private Harmony patcher;

        /// <summary>Default constructor</summary>
        public Main()
        {
            main = this;
        }
        // Implementation

        /// <summary>NameID</summary>
        public override string ModNameID => "UITools";

        /// <summary>DisplayName</summary>
        public override string DisplayName => "UI Tools";

        /// <summary>Author</summary>
        public override string Author => "StarMods";

        /// <summary>MinimumGameVersionNecessary</summary>
        public override string MinimumGameVersionNecessary => "1.5.10.2";

        /// <summary>ModVersion</summary>
        public override string ModVersion => "1.2";

        /// <summary>Description</summary>
        public override string Description =>
            "Mod that provides advanced UI functionality and used by other mods as dependency";

        /// <summary>Icon</summary>
        public override string IconLink => "https://i.imgur.com/r7rCmJT.jpg";

        /// <inheritdoc />
        public Dictionary<string, IFile> UpdatableFiles => new()
        {
            {
                "https://github.com/VerdiX094/UITools/releases/latest/download/UITools.dll",
                this.GetModFolder().GetFile("UITools.dll")
            }
        };

        /// <summary>Early Load</summary>
        public override void Early_Load()
        {
            PatchAll();
            ConfigurationMenu.Initialize();
            PositionSaver.Initialize();
        }

        /// <summary>
        ///     Load
        /// </summary>
        public override void Load()
        {
            if (!this.GetModFolder().GetFile("NO_MOD_UPDATE").Exists())
                ModsUpdater.StartUpdate();
        }

        private void PatchAll()
        {
            (patcher ??= new Harmony("UITools")).PatchAll();
        }
    }
}