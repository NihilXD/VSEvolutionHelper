namespace VSItemTooltips.Core.Models
{
    /// <summary>
    /// Pure C# representation of popup position calculation.
    /// No Unity/IL2CPP dependencies - fully testable.
    /// </summary>
    public class PopupPositionCalculator
    {
        private readonly float _screenWidth;
        private readonly float _screenHeight;

        public PopupPositionCalculator(float screenWidth, float screenHeight)
        {
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
        }

        /// <summary>
        /// Calculates the position for a popup to avoid screen edges.
        /// Pure business logic - easily testable.
        /// </summary>
        public (float x, float y) CalculatePosition(
            float anchorX, float anchorY,
            float popupWidth, float popupHeight,
            bool usingController = false)
        {
            float posX, posY;

            if (usingController)
            {
                // Controller mode: popup appears to the left
                posX = anchorX - (popupWidth * 0.5f);
                posY = anchorY + 15f;
            }
            else
            {
                // Mouse mode: popup appears at cursor
                posX = anchorX - 15f;
                posY = anchorY + 40f;
            }

            // Clamp to screen bounds
            float halfWidth = _screenWidth / 2;
            float halfHeight = _screenHeight / 2;

            // Right edge
            if (posX + popupWidth > halfWidth)
                posX = halfWidth - popupWidth;

            // Left edge
            if (posX < -halfWidth)
                posX = -halfWidth;

            // Top edge
            if (posY > halfHeight)
                posY = halfHeight;

            // Bottom edge
            if (posY - popupHeight < -halfHeight)
                posY = -halfHeight + popupHeight;

            return (posX, posY);
        }
    }
}
