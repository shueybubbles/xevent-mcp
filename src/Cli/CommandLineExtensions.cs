// Copyright (c) David Shiflet. All rights reserved.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;

namespace Bubbles.XEvent.Listen
{
    internal static class CommandLineExtensions
    {
        public static T GetValueOrEnvVar<T>(this ParseResult parseResult, string optionName, string envVarName, T defaultValue = default!) where T : struct, Enum
        {
            var value = parseResult.GetValue<T?>(optionName);
            if (value != null)
            {
                return (T)value;
            }

            var envVarValue = Environment.GetEnvironmentVariable(envVarName);
            if (envVarValue != null)
            {
                try
                {
                    return (T)Enum.Parse(typeof(T), envVarValue, ignoreCase: true);
                }
                catch
                {
                    throw new InvalidOperationException($"Invalid value '{envVarValue}' for environment variable '{envVarName}'. Expected one of: {string.Join(", ", Enum.GetNames(typeof(T)))}");
                }
            }

            return defaultValue;
        }

        public static long GetValueOrEnvVar(this ParseResult parseResult, string optionName, string envVarName, long defaultValue = 0)
        {
            var value = parseResult.GetValue<long?>(optionName);
            if (value != null)
            {
                return value.Value;
            }

            var envVarValue = Environment.GetEnvironmentVariable(envVarName);

            return !string.IsNullOrEmpty(envVarValue) && long.TryParse(envVarValue, out var parsedValue) ? parsedValue : defaultValue;
        }

        public static string GetValueOrEnvVar(this ParseResult parseResult, string optionName, string envVarName, string defaultValue = "")
        {
            var value = parseResult.GetValue<string>(optionName);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
            var envVarValue = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(envVarValue))
            {
                return envVarValue;
            }
            return defaultValue;
        }

        public static string[] GetValueOrEnvVar(this ParseResult parseResult, string optionName, string envVarName)
        {
            var value = parseResult.GetValue<string[]>(optionName);
            if (value != null && value.Length > 0)
            {
                return [.. value.SelectMany(item => item.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))];
            }

            var envVarValue = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(envVarValue))
            {
                var items = new List<string>();
                var current = new StringBuilder();
                var inQuotes = false;

                foreach (var ch in envVarValue)
                {
                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (!inQuotes && (char.IsWhiteSpace(ch) || ch == ','))
                    {
                        if (current.Length > 0)
                        {
                            items.Add(current.ToString());
                            current.Clear();
                        }

                        continue;
                    }

                    current.Append(ch);
                }

                if (current.Length > 0)
                {
                    items.Add(current.ToString());
                }

                return [.. items];
            }

            return [];
        }
    }
}
