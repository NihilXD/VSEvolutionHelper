namespace VSItemTooltips.Core.Abstractions
{
    /// <summary>
    /// Abstraction for logging operations.
    /// Allows capturing log output in tests.
    /// </summary>
    public interface IModLogger
    {
        void Msg(string message);
        void Warning(string message);
        void Error(string message);
    }
}
