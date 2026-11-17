using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RetroEye.Api.Tests
{
    /// <summary>
    /// Checks a JSON value against the parts of JSON Schema the contract's schema uses: type, required, properties,
    /// additionalProperties, items, enum, pattern, minimum, maximum, minItems and #/$defs references.
    /// </summary>
    internal static class SchemaCheck
    {
        /// <summary>Returns everything in value that breaks the schema, one line each (empty when it matches).</summary>
        public static List<string> Problems(ApiJson schema, ApiJson value)
        {
            var problems = new List<string>();
            Check(schema, schema, value, "$", problems);
            return problems;
        }

        /// <summary>Adds a line to problems for everything in value that breaks schema (root is the whole schema, for references).</summary>
        public static void Check(ApiJson root, ApiJson schema, ApiJson value, string path, List<string> problems)
        {
            string reference = schema["$ref"].AsStringOrNull();
            if (reference != null)
            {
                Check(root, root["$defs"][reference.Substring("#/$defs/".Length)], value, path, problems);
                return;
            }
            ApiJson type = schema["type"];
            if (!type.IsNull)
            {
                var allowed = type.Kind == ApiJsonKind.Array ? type.Items.Select(t => t.AsString()).ToList() : new List<string> { type.AsString() };
                if (!allowed.Any(t => Is(t, value)))
                {
                    problems.Add($"{path}: is {value.Kind}, not {string.Join("/", allowed)}");
                    return;
                }
            }
            if (value.IsNull)
            {
                return;
            }
            ApiJson choices = schema["enum"];
            if (choices.Kind == ApiJsonKind.Array && !choices.Items.Any(c => c.ToString() == value.ToString()))
            {
                problems.Add($"{path}: {value} isn't one of {choices}");
            }
            if (schema["pattern"].AsStringOrNull() is string pattern && value.Kind == ApiJsonKind.String && !Regex.IsMatch(value.AsString(), pattern))
            {
                problems.Add($"{path}: '{value.AsString()}' doesn't match {pattern}");
            }
            if (value.Kind == ApiJsonKind.Number)
            {
                if (schema["minimum"].Kind == ApiJsonKind.Number && value.AsNumber() < schema["minimum"].AsNumber()) problems.Add($"{path}: {value} < minimum");
                if (schema["maximum"].Kind == ApiJsonKind.Number && value.AsNumber() > schema["maximum"].AsNumber()) problems.Add($"{path}: {value} > maximum");
            }
            if (value.Kind == ApiJsonKind.Object)
            {
                foreach (ApiJson required in schema["required"].Items)
                {
                    if (!value.Has(required.AsString()))
                    {
                        problems.Add($"{path}: no {required.AsString()}");
                    }
                }
                foreach (var member in value.Members)
                {
                    ApiJson own = schema["properties"][member.Key];
                    if (!own.IsNull)
                    {
                        Check(root, own, member.Value, path + "." + member.Key, problems);
                    }
                    else if (!schema["additionalProperties"].IsNull && schema["additionalProperties"].Kind == ApiJsonKind.Object)
                    {
                        Check(root, schema["additionalProperties"], member.Value, path + "." + member.Key, problems);
                    }
                }
            }
            if (value.Kind == ApiJsonKind.Array)
            {
                if (schema["minItems"].Kind == ApiJsonKind.Number && value.Items.Count < schema["minItems"].AsInt()) problems.Add($"{path}: too few items");
                if (!schema["items"].IsNull)
                {
                    for (int i = 0; i < value.Items.Count; i++)
                    {
                        Check(root, schema["items"], value.Items[i], $"{path}[{i}]", problems);
                    }
                }
            }
        }

        /// <summary>Whether a value is of a schema type.</summary>
        private static bool Is(string type, ApiJson value) => type switch
        {
            "object" => value.Kind == ApiJsonKind.Object,
            "array" => value.Kind == ApiJsonKind.Array,
            "string" => value.Kind == ApiJsonKind.String,
            "number" => value.Kind == ApiJsonKind.Number,
            "integer" => value.Kind == ApiJsonKind.Number && value.AsNumber() == Math.Floor(value.AsNumber()),
            "boolean" => value.Kind == ApiJsonKind.Boolean,
            "null" => value.IsNull,
            _ => false,
        };
    }
}
