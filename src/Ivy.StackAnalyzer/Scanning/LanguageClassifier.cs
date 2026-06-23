using Ivy.StackAnalyzer.Models;

namespace Ivy.StackAnalyzer.Scanning;

/// <summary>A scanned file together with its resolved language (if any).</summary>
public sealed record ClassifiedFile
{
    public required ScannedFile File { get; init; }
    public string? Language { get; init; }
    public LanguageType? Type { get; init; }
}

/// <summary>
/// Resolves a file's language using <c>languages.yml</c> via, in order:
/// exact filename, extension, then shebang interpreter. Where an extension is
/// shared by several languages the choice is deterministic (programming type
/// preferred, then ordinal by name).
/// </summary>
public sealed class LanguageClassifier
{
    private readonly DataStore _data;

    public LanguageClassifier(DataStore data) => _data = data;

    public ClassifiedFile Classify(ScannedFile file)
    {
        var name = ResolveLanguage(file);
        if (name is null)
            return new ClassifiedFile { File = file };

        // A binary blob whose extension happens to map to a language (e.g. a Python
        // pickle `.p` → Gnuplot) must not be counted as source code.
        if (IsBinary(file.FullPath))
            return new ClassifiedFile { File = file };

        return new ClassifiedFile
        {
            File = file,
            Language = name,
            Type = ParseType(_data.Languages[name].Type),
        };
    }

    // Cheap binary sniff: a NUL byte in the first few KB is a strong binary signal
    // (UTF-8/16 text without a BOM never contains a lone NUL in normal content).
    private static bool IsBinary(string fullPath)
    {
        try
        {
            using var fs = File.OpenRead(fullPath);
            Span<byte> buf = stackalloc byte[4096];
            int n = fs.Read(buf);
            for (int i = 0; i < n; i++)
                if (buf[i] == 0) return true;
            return false;
        }
        catch { return false; }
    }

    private string? ResolveLanguage(ScannedFile file)
    {
        // 1. Exact filename (Dockerfile, Makefile, go.mod, ...)
        if (_data.ByFilename.TryGetValue(file.FileName, out var byName))
            return byName;

        // 2. Extension
        var ext = file.Extension;
        if (ext.Length > 0 && _data.ByExtension.TryGetValue(ext, out var candidates))
            return PickBest(candidates);

        // 3. Shebang interpreter (only when extension didn't resolve)
        var interp = ReadShebangInterpreter(file.FullPath);
        if (interp is not null && _data.ByInterpreter.TryGetValue(interp, out var byInterp))
            return byInterp;

        return null;
    }

    // Common languages that should win an extension collision over obscure ones
    // sharing the same extension (e.g. ".md" -> Markdown, not "GCC Machine
    // Description"). Lower index = higher priority. This is the pragmatic stand-in
    // for linguist's heuristics, which we deliberately do not port.
    // Ordered by real-world popularity so that the more common language wins an
    // extension collision (e.g. ".ts" -> TypeScript, not Qt-Linguist "XML").
    private static readonly string[] CommonPriority =
    [
        "Markdown", "JSON", "YAML", "TypeScript", "TSX", "JavaScript", "JSX",
        "Python", "C#", "F#", "Java", "Kotlin", "Go", "Rust", "C", "C++", "OpenCL",
        "Ruby", "PHP", "Swift", "Scala", "HTML", "CSS", "SCSS", "Sass", "Less",
        "Vue", "Svelte", "Dart", "Elixir", "Shell", "PowerShell", "SQL", "XML",
        "Objective-C", "Dockerfile", "Text",
    ];

    private string PickBest(List<string> candidates)
    {
        if (candidates.Count == 1) return candidates[0];

        // 1. A well-known common language wins ties outright.
        var common = candidates
            .Select(c => (Name: c, Rank: Array.IndexOf(CommonPriority, c)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ToList();
        if (common.Count > 0) return common[0].Name;

        // 2. Prefer the language for which this extension is its primary one,
        //    then programming over non-programming, then ordinal for determinism.
        return candidates
            .OrderByDescending(c => ParseType(_data.Languages[c].Type) == LanguageType.Programming)
            .ThenBy(c => c, StringComparer.Ordinal)
            .First();
    }

    private static string? ReadShebangInterpreter(string fullPath)
    {
        try
        {
            using var reader = new StreamReader(fullPath);
            // Read a bounded prefix only: shebangs are short, and the file may be a
            // huge single-line binary/data blob we must not load into memory.
            var buf = new char[256];
            int n = reader.Read(buf, 0, buf.Length);
            if (n <= 0) return null;
            var head = new string(buf, 0, n);
            var nl = head.IndexOfAny(['\n', '\r']);
            var first = nl >= 0 ? head[..nl] : head;
            if (!first.StartsWith("#!")) return null;
            // "#!/usr/bin/env python3" -> python3 ; "#!/bin/bash" -> bash
            var parts = first[2..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var exe = parts[0].Split('/').Last();
            if (exe is "env" && parts.Length > 1)
                exe = parts[1].Split('/').Last();
            return exe;
        }
        catch { return null; }
    }

    public static LanguageType ParseType(string type) => type.ToLowerInvariant() switch
    {
        "markup" => LanguageType.Markup,
        "data" => LanguageType.Data,
        "prose" => LanguageType.Prose,
        _ => LanguageType.Programming,
    };
}
