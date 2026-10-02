// src/platform/store.mjs: browser storage that tells you when it failed. C# 9.
//
// localStorage throws when the quota is gone or the browser refuses to store anything (private
// windows, blocked site data). Most of the port's state can lose a write without harm - a remembered
// filter, the last spell power - and those keep their own quiet catch. The paths that move things
// between two places (the chest, the save slots) must not: an unreported failure there destroys or
// duplicates what the player owns.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LolHost
{
    public static class Store
    {
        /// <summary>window.localStorage: the host sets it at startup (web.localStorage).</summary>
        public static LocalStorage storage;

        static string lastFailure = "";
        static readonly HashSet<Action<string>> listeners = new HashSet<Action<string>>();

        /// <summary>JSON.stringify's output: public fields under their JS names, undefined (null) fields
        /// left out, and no escaping of non-ASCII or HTML characters, so the browser reads what this writes.</summary>
        public static readonly JsonSerializerOptions json = new JsonSerializerOptions
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>JSON.stringify(value) (a JsonNode, or an object with public fields).</summary>
        public static string stringify(object value)
        {
            if (value == null) return "null";
            if (value is JsonNode node) return node.ToJsonString(json);
            return JsonSerializer.Serialize(value, value.GetType(), json);
        }

        public static JsonNode readJson(string key, JsonNode fallback = null)
        {
            try
            {
                string raw = storage.getItem(key);
                if (raw == null) return fallback;
                JsonNode value = JsonNode.Parse(raw);
                return value;   // JSON.parse never gives undefined
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        // Returns true when the value is really in storage, false (and reports) when it is not.
        public static bool writeJson(string key, object value)
        {
            return writeRaw(key, stringify(value));
        }

        public static bool writeRaw(string key, string text)
        {
            try
            {
                storage.setItem(key, text);
                return true;
            }
            catch (Exception error)
            {
                report($"{key}: {(!string.IsNullOrEmpty(error.Message) ? error.Message : "storage refused the write")}");
                return false;
            }
        }

        public static bool removeKey(string key)
        {
            try
            {
                storage.removeItem(key);
                return true;
            }
            catch (Exception error)
            {
                report($"{key}: {(!string.IsNullOrEmpty(error.Message) ? error.Message : "storage refused")}");
                return false;
            }
        }

        // What went wrong last, for a message the player can act on ("free some space and try again").
        public static string lastStorageFailure() { return lastFailure; }

        public static Action onStorageFailure(Action<string> fn) { listeners.Add(fn); return () => listeners.Remove(fn); }

        static void report(string message)
        {
            lastFailure = message;
            foreach (var fn in listeners.ToList()) { try { fn(message); } catch (Exception) { /* a listener must not break a write */ } }
        }

        // ---- port helpers (JS coercions the platform modules rely on) ----

        /// <summary>JS Number(v) on a JSON value (undefined -> NaN, null -> 0).</summary>
        public static double jsNumber(JsonNode v)
        {
            if (v == null) return 0;   // Number(null); a missing key is passed as undefined via jsNumberOf
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue(out double d)) return d;
                if (jv.TryGetValue(out bool b)) return b ? 1 : 0;
                if (jv.TryGetValue(out string s))
                {
                    s = s.Trim();
                    if (s.Length == 0) return 0;
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double ds) ? ds : double.NaN;
                }
            }
            return double.NaN;
        }

        /// <summary>Number(obj[key]): a key the object does not have is undefined (NaN).</summary>
        public static double jsNumberOf(JsonNode obj, string key)
        {
            if (!(obj is JsonObject o) || !o.TryGetPropertyValue(key, out JsonNode v)) return double.NaN;
            return jsNumber(v);
        }

        /// <summary>Math.trunc(x) || 0</summary>
        public static int truncOr0(double x) => double.IsNaN(x) || double.IsInfinity(x) ? 0 : (int)Math.Truncate(x);

        /// <summary>JS truthiness of a JSON value (undefined/null, false, 0, NaN and "" are falsy).</summary>
        public static bool truthy(JsonNode v)
        {
            if (v == null) return false;
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue(out bool b)) return b;
                if (jv.TryGetValue(out double d)) return d != 0 && !double.IsNaN(d);
                if (jv.TryGetValue(out string s)) return s.Length > 0;
            }
            return true;
        }
    }
}
