using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using StoreChase;

namespace StoreChase.Scripting;

/// <summary>
/// Host for compiling and caching C# scripts using Roslyn.
/// Scripts are compiled once and cached by file path + content hash.
/// </summary>
public static class ScriptHost
{
    private static readonly ConcurrentDictionary<string, CachedScript> _cache = new();
    private static readonly ScriptOptions _baseOptions;
    private static readonly Assembly[] _referenceAssemblies;

    static ScriptHost()
    {
        // Core assemblies the scripts can reference
        _referenceAssemblies = new[]
        {
            typeof(object).Assembly,                    // System.Runtime
            typeof(Enumerable).Assembly,                // System.Linq
            typeof(List<>).Assembly,                    // System.Collections
            typeof(ValueTuple<>).Assembly,              // System.ValueTuple
            typeof(Microsoft.Xna.Framework.Vector2).Assembly, // MonoGame
            typeof(Game1).Assembly,                     // StoreChase
            typeof(ICritterAI).Assembly,                // StoreChase (AI types)
            typeof(ScriptHost).Assembly,                // StoreChase.Scripting
        }.Distinct().ToArray();

        _baseOptions = ScriptOptions.Default
            .WithReferences(_referenceAssemblies)
            .WithImports(
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "Microsoft.Xna.Framework",
                "StoreChase",
                "StoreChase.Scripting");
    }

    /// <summary>
    /// Compiles a script file and returns a delegate of type T.
    /// Caches by file path + content hash for hot-reload support.
    /// </summary>
    public static T Compile<T>(string scriptPath, string[]? additionalImports = null) where T : Delegate
    {
        string fullPath = Path.GetFullPath(scriptPath);
        string content = File.ReadAllText(fullPath);
        string cacheKey = $"{fullPath}|{ComputeHash(content)}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Value is T typed)
            return typed;

        var options = _baseOptions;
        if (additionalImports?.Length > 0)
            options = options.WithImports(options.Imports.Concat(additionalImports));

        // The script must be a single expression or statement block that produces T
        // We wrap it in a factory method pattern
        string scriptCode = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.Xna.Framework;
            using StoreChase;
            using StoreChase.Scripting;

            {{content}}
            """;

        try
        {
            var script = CSharpScript.Create<T>(scriptCode, options);
            var compilation = script.Compile();
            
            if (compilation.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = compilation.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Id}: {d.GetMessage()}");
                throw new ScriptCompilationException(fullPath, errors);
            }

            var runner = script.CreateDelegate();
            var result = runner(null).Result;

            _cache[cacheKey] = new CachedScript(fullPath, content, result);
            return result;
        }
        catch (CompilationErrorException ex)
        {
            throw new ScriptCompilationException(fullPath, ex.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}: {d.GetMessage()}"));
        }
        catch (Exception ex) when (ex is not ScriptCompilationException)
        {
            throw new ScriptCompilationException(fullPath, new[] { ex.Message });
        }
    }

    /// <summary>
    /// Compiles a script that returns an object implementing an interface.
    /// The script should define a class and return `new MyClass()` as the last expression.
    /// </summary>
    public static T CompileInstance<T>(string scriptPath, string[]? additionalImports = null) where T : class
    {
        string fullPath = Path.GetFullPath(scriptPath);
        string content = File.ReadAllText(fullPath);
        string cacheKey = $"{fullPath}|{ComputeHash(content)}|instance";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Value is T typed)
            return typed;

        var options = _baseOptions;
        if (additionalImports?.Length > 0)
            options = options.WithImports(options.Imports.Concat(additionalImports));

        string scriptCode = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.Xna.Framework;
            using StoreChase;
            using StoreChase.Scripting;

            {{content}}
            """;

        try
        {
            var script = CSharpScript.Create<T>(scriptCode, options);
            var compilation = script.Compile();
            
            if (compilation.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = compilation.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Id}: {d.GetMessage()}");
                throw new ScriptCompilationException(fullPath, errors);
            }

            var runner = script.CreateDelegate();
            var task = runner(null);
            var result = task.Result;

            _cache[cacheKey] = new CachedScript(fullPath, content, result);
            return result;
        }
        catch (CompilationErrorException ex)
        {
            throw new ScriptCompilationException(fullPath, ex.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}: {d.GetMessage()}"));
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScriptCompilationException(fullPath, new[] { $"Runtime error: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}" });
        }
        catch (Exception ex)
        {
            throw new ScriptCompilationException(fullPath, new[] { $"Runtime error: {ex.Message}\n{ex.StackTrace}" });
        }
    }

    /// <summary>
    /// Invalidates the cache for a specific script (for hot-reload).
    /// </summary>
    public static void Invalidate(string scriptPath)
    {
        string fullPath = Path.GetFullPath(scriptPath);
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith(fullPath + "|")).ToList();
        foreach (var key in keysToRemove)
            _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Clears all cached scripts.
    /// </summary>
    public static void ClearCache() => _cache.Clear();

    private static string ComputeHash(string content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..16];
    }

    private sealed class CachedScript
    {
        public readonly string Path;
        public readonly string Content;
        public readonly object Value;

        public CachedScript(string path, string content, object value)
        {
            Path = path;
            Content = content;
            Value = value;
        }
    }
}

/// <summary>
/// Exception thrown when a script fails to compile.
/// </summary>
public sealed class ScriptCompilationException : Exception
{
    public string ScriptPath { get; }
    public IReadOnlyList<string> Errors { get; }

    public ScriptCompilationException(string scriptPath, IEnumerable<string> errors)
        : base($"Script compilation failed: {scriptPath}\n{string.Join("\n", errors)}")
    {
        ScriptPath = scriptPath;
        Errors = errors.ToList();
    }
}

/// <summary>
/// Global script state accessible to all scripts.
/// Provides services like logging, RNG, and event publishing.
/// </summary>
public static class ScriptServices
{
    private static Random? _rng;
    private static EventSystem? _events;
    private static Action<string>? _log;

    public static void Initialize(Random rng, EventSystem events, Action<string> log)
    {
        _rng = rng;
        _events = events;
        _log = log;
    }

    public static Random Rng => _rng ?? throw new InvalidOperationException("ScriptServices not initialized");
    public static EventSystem Events => _events ?? throw new InvalidOperationException("ScriptServices not initialized");
    
    public static void Log(string message) => _log?.Invoke($"[Script] {message}");
    
    public static void Publish<T>(T evt) where T : BaseEvent => Events.Publish(evt);
}