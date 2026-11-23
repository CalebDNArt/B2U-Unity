using UnityEngine;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace B2U
{
    /// <summary>
    /// Provides formatted debugging for B2U functions. Automatically trimmed from builds to prevent resource usage on logging unless a build keyword "ENABLE_B2U_LOGS" is added.
    /// </summary>
    public static class B2UDebug
    {
        /// <summary>
        /// Generic B2U log function.
        /// </summary>
        /// <param name="source">The message source for the log.</param>
        /// <param name="message">The message.</param>
        /// <param name="type">The type of message.</param>
        /// <param name="context">The object which called the function that triggered the log.</param>
        /// <param name="caller">The calling method that triggered the log.</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_B2U_LOGS")]
        public static void B2ULog(B2UMessageSource source, string message, B2UMessageType type = B2UMessageType.None, UnityEngine.Object context = null, [CallerMemberName] string caller = "")
        {
            string colorHex = source == B2UMessageSource.Editor ? "#42f593" : "#42cbf5";

            string msg = $"<color={colorHex}><b>[B2U {source.ToString()}]</b></color> {caller}(): {message}";

            switch (type)
            {
                case B2UMessageType.None:
                    UnityEngine.Debug.Log(msg, context);
                    break;
                case B2UMessageType.Warning:
                    UnityEngine.Debug.LogWarning(msg, context);
                    break;
                case B2UMessageType.Error:
                    UnityEngine.Debug.LogError(msg, context);
                    break;
            }
        }
        /// <summary>
        /// Not yet implemented in pipeline. Asserts a condition to mark areas where a value is false.
        /// </summary>
        /// <param name="condition">The condtion to check.</param>
        /// <param name="source">The message source for the log.</param>
        /// <param name="message">The message.</param>
        /// <param name="context">The object which called the function that triggered the log.</param>
        /// <param name="caller">The calling method that triggered the log.</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_B2U_LOGS")]
        public static void B2UAssert(bool condition, B2UMessageSource source, string message, UnityEngine.Object context = null, [CallerMemberName] string caller = "")
        {
            if (!condition)
            {
                B2ULog(source, $"ASSERTION FAILED: {message}", B2UMessageType.Error, context, caller);
            }
        }
        /// <summary>
        /// Shorthand function to log a warning.
        /// </summary>
        /// <param name="source">The message source for the log.</param>
        /// <param name="message">The message.</param>
        /// <param name="context">The object which called the function that triggered the log.</param>
        /// <param name="caller">The calling method that triggered the log.</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_B2U_LOGS")]
        public static void B2ULogWarning(B2UMessageSource source, string message, UnityEngine.Object context = null, [CallerMemberName] string caller = "")
        {
            B2ULog(source, message, B2UMessageType.Warning, context, caller);
        }
        /// <summary>
        /// Shorthand function to log an error.
        /// </summary>
        /// <param name="source">The message source for the log.</param>
        /// <param name="message">The message.</param>
        /// <param name="context">The object which called the function that triggered the log.</param>
        /// <param name="caller">The calling method that triggered the log.</param>
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_B2U_LOGS")]
        public static void B2ULogError(B2UMessageSource source, string message, UnityEngine.Object context = null, [CallerMemberName] string caller = "")
        {
            B2ULog(source, message, B2UMessageType.Error, context, caller);
        }
        /// <summary>
        /// The message type.
        /// </summary>
        public enum B2UMessageType
        {
            /// <summary>
            /// A generic message for information.
            /// </summary>
            None,
            /// <summary>
            /// A warning that something was unavailable or unable to be performed.
            /// </summary>
            Warning,
            /// <summary>
            /// An error that will likely cause runtime issues.
            /// </summary>
            Error
        }
        /// <summary>
        /// The message source for a B2U Log.
        /// </summary>
        public enum B2UMessageSource
        {
            /// <summary>
            /// An editor script is the caller.
            /// </summary>
            Editor,
            /// <summary>
            /// A runtime script is the caller.
            /// </summary>
            Runtime
        }
    }

    /// <summary>
    /// A profiler for checking the runtime performance of a method or script.
    /// </summary>
    public struct B2UProfiler : System.IDisposable
    {
        /// <summary>
        /// The name of the block which invoked the profiler.
        /// </summary>
        private string blockName;
        /// <summary>
        /// The message source for the log.
        /// </summary>
        private B2UDebug.B2UMessageSource source;
        /// <summary>
        /// A stopwatch to count the time it takes to execute.
        /// </summary>
        private Stopwatch stopwatch;

        public B2UProfiler(string blockName, B2UDebug.B2UMessageSource source = B2UDebug.B2UMessageSource.Runtime)
        {
            this.blockName = blockName;
            this.source = source;
            this.stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            stopwatch.Stop();
            B2UDebug.B2ULog(source, $"[Profiler] {blockName} took {stopwatch.Elapsed.TotalMilliseconds:F4} ms.");
        }
    }
}
