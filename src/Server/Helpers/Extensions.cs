using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bubbles.XEvent.MCPServer.Helpers
{
    internal static class Extensions
    {
        /// <summary>
        /// Converts a comma-separated string into a HashSet of strings, trimming entries and removing empty entries. The resulting HashSet uses case-insensitive string comparison.
        /// </summary>
        /// <param name="list">The comma-separated string to convert.</param>
        /// <returns>A HashSet of strings.</returns>
        public static HashSet<string> ToHashSet(this string list) => 
            list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
