using System;
using JetBrains.Annotations;
using SFS.IO;
using SFS.Parsers.Json;

namespace UITools
{
    /// <summary>
    ///     Abstract class that let you easily create your mod configuration
    /// </summary>
    /// <typeparam name="T">Data type which will be stored in config file</typeparam>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public abstract class ModSettings<T> where T : new()
    {
        /// <summary>
        ///     Static variable for getting settings
        /// </summary>
        public static T settings;

        /// <summary>
        ///     Getting settings file path
        /// </summary>
        [Obsolete("Override " + nameof(ConfigFile) + " instead, which uses the IFile storage API.")]
        protected virtual FilePath SettingsFile => null;

        /// <summary>
        ///     The file your settings are stored in
        /// </summary>
        protected virtual IFile ConfigFile => null;

        // Falls back to the deprecated property so existing overrides keep working
#pragma warning disable 618
        IFile GetConfigFile()
        {
            IFile file = ConfigFile ?? SettingsFile.ToStorageFile();

            if (file == null)
                throw new InvalidOperationException(
                    $"{GetType().Name} overrides neither {nameof(ConfigFile)} nor {nameof(SettingsFile)}, " +
                    "so there is nowhere to store its settings.");

            return file;
        }
#pragma warning restore 618

        void Load()
        {
            IFile file = GetConfigFile();
            settings = file.Exists() ? JsonWrapper.FromJson<T>(file.ReadText()) : new T();
            settings ??= new T();
        }

        void Save()
        {
            GetConfigFile().WriteText(JsonWrapper.ToJson(settings, true));
        }


        /// <summary>
        ///     You should call this function after creating an instance of class
        /// </summary>
        public void Initialize()
        {
            Load();
            Save();
            RegisterOnVariableChange(Save);
        }

        /// <summary>
        ///     Allow you to subscribe save event to config variables change
        /// </summary>
        /// <param name="onChange">Action that you should subscribe to data variables onChange</param>
        protected abstract void RegisterOnVariableChange(Action onChange);
    }
}