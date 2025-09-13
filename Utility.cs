using System;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using SFS.Input;
using SFS.UI;
using SFS.UI.ModGUI;
using SFS.Variables;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UITools
{
    /// <summary>
    ///     Observable GameObject variable
    /// </summary>
// ReSharper disable once InconsistentNaming
    public class GameObject_Local : Obs<GameObject>
    {
        /// <summary>Comparison</summary>
        protected override bool IsEqual(GameObject a, GameObject b)
        {
            return a == b;
        }
    }

    /// <summary>
    /// Represents the anchor and origin of the window
    /// </summary>
    public enum AnchorOrigin
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        
        // 0x11 0x12 0x24
        // 0x20 0x22 0x24
        // 0x40 0x42 0x44
        
        TopLeft = 0x11,
        TopCenter = 0x12,
        TopRight = 0x14,
        
        MiddleLeft = 0x21,
        MiddleCenter = 0x22,
        MiddleRight = 0x24,
        
        BottomLeft = 0x41,
        BottomCenter = 0x42,
        BottomRight = 0x44,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }

    /// <summary>
    ///     Utility for UI
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public static class UIUtility
    {
        static RectTransform canvas;

        /// <summary>
        ///     Rect Transform of the canvas
        /// </summary>
        public static RectTransform CanvasRectTransform => canvas ??= GetCanvasRect();

        /// <summary>
        ///     Get the canvas pixel size
        /// </summary>
        public static Vector2 CanvasPixelSize
        {
            get
            {
                canvas ??= GetCanvasRect();
                return canvas.sizeDelta;
            }
        }

        static RectTransform GetCanvasRect()
        {
            GameObject temp = Builder.CreateHolder(Builder.SceneToAttach.BaseScene, "TEMP");
            RectTransform result = temp.transform.parent as RectTransform;
            Object.Destroy(temp);
            return result;
        }

        /// <summary>
        ///     Creates texture from base64 string
        /// </summary>
        public static Texture2D CreateTexture(string base64Image)
        {
            byte[] data = Convert.FromBase64String(base64Image);
            Texture2D result = new(1, 1, TextureFormat.ARGB32, 1, true);
            result.LoadImage(data);
            result.Apply();
            return result;
        }

        /// <summary>
        /// Calculates the SFS-friendly window position from given coordinates and anchor/origin.
        /// Remember that both this function and SFS use Right-Up as the coordinate direction.
        /// </summary>
        /// <param name="posX">The horizontal position of the window's origin relative to the screen anchor.</param>
        /// <param name="posY">The vertical position of the window's origin relative to the screen anchor.</param>
        /// <param name="width">The width of your window (used for conversion of origins to upper-center window origin used by SFS)</param>
        /// <param name="height">The height of your window (used for conversion of origins to upper-center window origin used by SFS)</param>
        /// <param name="screenAnchor">The edge/corner of the screen that the position should be relative to.</param>
        /// <param name="windowOrigin">The edge/corner of the window that should have this position.</param>
        /// <returns>The upper-center relative position that can be used with Builder.CreateWindow or UIToolsBuilder.CreateClosableWindow.</returns>
        public static Vector2Int AnchorOriginToModGUI(int posX, int posY, int width, int height, AnchorOrigin screenAnchor = AnchorOrigin.MiddleCenter, AnchorOrigin windowOrigin = AnchorOrigin.TopCenter)
        {
            Vector2Int result = new(posX, posY);
            
            // ORIGINS
            
            if (((int)windowOrigin & 0x20) != 0) // Center origin, add half window height
                result.y += height / 2;
            if (((int)windowOrigin & 0x40) != 0) // Bottom origin, add full window height
                result.y += height;
            if (((int)windowOrigin & 0x01) != 0) // Left origin, add half window width (so top-center goes to the right)
                result.x += width / 2;
            if (((int)windowOrigin & 0x04) != 0) // Right origin, subtract half window width
                result.x -= width / 2;
            
            // ANCHORS
            
            if (((int)screenAnchor & 0x10) != 0) // Top anchor, add half screen height
                result.y += (int)CanvasPixelSize.y / 2;
            if (((int)screenAnchor & 0x40) != 0) // Bottom anchor, subtract half screen height
                result.y -= (int)CanvasPixelSize.y / 2;
            if (((int)screenAnchor & 0x01) != 0) // Left anchor, subtract half screen width
                result.x -= (int)CanvasPixelSize.x / 2;
            if (((int)screenAnchor & 0x04) != 0) // Right anchor, add half screen width
                result.x += (int)CanvasPixelSize.x / 2;
            
            return result;
        }
    }

    /// <summary>
    ///     Opens MenuGenerator as async functions
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    [Obsolete("MenuGenerator has async functions now")]
    public static class AsyncDialogs
    {
        /// <summary>
        ///     Works same as MenuGenerator.OpenConfirmation. Returns true if the user pressed the confirm button.
        /// </summary>
        [Obsolete("MenuGenerator has async functions now")]
        public static async UniTask<bool> OpenConfirmation(CloseMode closeMode, Func<string> text,
            Func<string> confirmText)
        {
            ConfirmationAwaiter awaiter = new();
            MenuGenerator.OpenConfirmation(closeMode, text, confirmText, () => awaiter.OnAction(true),
                onDeny: () => awaiter.OnAction(false));
            return await awaiter.WaitForConfirmation();
        }

        class ConfirmationAwaiter
        {
            bool closed;
            bool result;

            internal async UniTask<bool> WaitForConfirmation()
            {
                while (!closed)
                    await UniTask.Yield();
                return result;
            }

            internal void OnAction(bool res)
            {
                closed = true;
                result = res;
            }
        }
    }
}