using ModLoader;
using SFS.IO;

namespace UITools
{
    // Bridges the deprecated SFS.IO path types to the IFile/IFolder API
    internal static class StorageUtility
    {
        internal static IFolder GetModFolder(this Mod mod)
        {
            return new DefaultFolder(mod.ModFolder);
        }

        // Must go through the parent folder: a DefaultFile without one throws when written to
        internal static IFile ToStorageFile(this FilePath path)
        {
            return path == null ? null : new DefaultFolder(path.GetParent()).GetFile(path.FileName);
        }
    }
}
