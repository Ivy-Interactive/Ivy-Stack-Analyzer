using Ivy.StackAnalyzer.Models;
using Ivy.StackAnalyzer.Scanning;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

/// <summary>
/// Content-disambiguation tests, mirroring the per-extension expectations of
/// github-linguist's <c>test_heuristics.rb</c> (which language each ambiguous
/// extension resolves to). Inputs are minimal idiomatic snippets exercising each
/// rule rather than linguist's vendored fixture files.
/// </summary>
public class HeuristicsTests
{
    private static readonly Heuristics H = new(DataStore.Load().HeuristicsData);

    [Theory]
    // .m  — Objective-C / MATLAB / Mercury (the ones our corpus hits)
    [InlineData(".m", "Objective-C", "#import <Foundation/Foundation.h>\n@interface Foo : NSObject\n@end\n")]
    [InlineData(".m", "MATLAB", "function y = f(x)\n% increment\ny = x + 1;\nend\n")]
    [InlineData(".m", "Mercury", ":- module foo.\n:- interface.\n")]
    // .pl — Prolog vs Perl
    [InlineData(".pl", "Prolog", "parent(tom, bob).\nancestor(X, Y) :- parent(X, Y).\n")]
    [InlineData(".pl", "Perl", "use strict;\nuse warnings;\nmy $x = 1;\n")]
    // .v  — Verilog vs Rocq Prover (Coq)
    [InlineData(".v", "Verilog", "module counter(input clk, output reg [3:0] q);\nalways @(posedge clk) q <= q + 1;\nendmodule\n")]
    [InlineData(".v", "Rocq Prover", "Require Import List.\nTheorem t : True.\nProof. exact I. Qed.\n")]
    // .h  — Objective-C / C++ / C (catch-all)
    [InlineData(".h", "Objective-C", "#import <Foundation/Foundation.h>\n@interface Bar : NSObject\n@end\n")]
    [InlineData(".h", "C++", "#include <vector>\ntemplate <class T> class Vec {};\n")]
    [InlineData(".h", "C", "#include <stdio.h>\nint main(void) { return 0; }\n")]
    // .pro — Proguard / Prolog / QMake / IDL
    [InlineData(".pro", "Proguard", "-keep class com.example.** { *; }\n")]
    [InlineData(".pro", "QMake", "HEADERS += foo.h\nSOURCES += foo.cpp\n")]
    // .cls — TeX / VBA
    [InlineData(".cls", "TeX", "\\NeedsTeXFormat{LaTeX2e}\n\\ProvidesClass{foo}\n")]
    [InlineData(".cls", "VBA", "VERSION 1.0 CLASS\nAttribute VB_Name = \"Foo\"\n")]
    // .r  — R vs Rebol
    [InlineData(".r", "R", "x <- 1\n# a comment\n")]
    [InlineData(".r", "Rebol", "Rebol [Title: \"x\"]\nprint \"hi\"\n")]
    // .t  — Perl vs Turing
    [InlineData(".t", "Perl", "use strict;\nmy $x = 1;\n")]
    [InlineData(".t", "Turing", "var x : int := 0\n")]
    public void Disambiguates_extension_by_content(string ext, string expected, string content)
    {
        var result = H.Disambiguate(ext, content);
        Assert.NotNull(result);
        Assert.Equal(expected, result![0]);
    }

    [Fact]
    public void Returns_null_when_no_rule_matches()
    {
        // A .m file with no Objective-C / MATLAB / Mercury / ... marker is inconclusive;
        // the classifier then keeps its own popularity fallback.
        Assert.Null(H.Disambiguate(".m", "x = 1 + 1\n"));
        // An extension with no heuristics group at all returns null too.
        Assert.Null(H.Disambiguate(".cs", "class C {}\n"));
    }

    [Fact]
    public void Negative_pattern_and_and_block_reject_v6_perl_as_raku()
    {
        // The Perl rule is `and: [negative_pattern v6, named_pattern perl]`. A Raku
        // (v6) file must be REJECTED by the negative pattern and fall through to Raku.
        var result = H.Disambiguate(".pl", "use v6;\nmy class Foo {}\n");
        Assert.Equal("Raku", result?[0]);
    }

    [Fact]
    public void Catch_all_rule_resolves_h_to_c_when_inconclusive()
    {
        // The `.h` group ends with a pattern-less `C` rule (linguist's catch-all):
        // content with neither Objective-C nor C++ markers must resolve to C, not null.
        var result = H.Disambiguate(".h", "int x = 0;\nvoid f(void);\n");
        Assert.Equal("C", result?[0]);
    }

    [Fact]
    public void Classifier_normalizes_crlf_for_anchored_heuristics()
    {
        // The Wolfram `.m` rule is `and: ['\(\*', '\*\)$']` — the `$` anchor only matches
        // a CRLF line if \r is normalized away first. Proves ReadHead's CRLF handling.
        var classifier = new LanguageClassifier(DataStore.Load());
        var tmp = Path.Combine(Path.GetTempPath(), "ivy-crlf-" + Guid.NewGuid().ToString("N") + ".m");
        System.IO.File.WriteAllText(tmp, "(* a Wolfram comment *)\r\nx = 1\r\n");
        try
        {
            var c = classifier.Classify(new ScannedFile { RelativePath = "a.m", FullPath = tmp, Length = 24 });
            Assert.Equal("Wolfram Language", c.Language);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void Binary_file_is_not_disambiguated()
    {
        // A .m file with NUL bytes must short-circuit via the binary check before any
        // heuristic runs — language stays null rather than being disambiguated.
        var classifier = new LanguageClassifier(DataStore.Load());
        var tmp = Path.Combine(Path.GetTempPath(), "ivy-binm-" + Guid.NewGuid().ToString("N") + ".m");
        System.IO.File.WriteAllBytes(tmp, [0x00, 0x01, (byte)'%', 0x00, 0x02]);
        try
        {
            var c = classifier.Classify(new ScannedFile { RelativePath = "a.m", FullPath = tmp, Length = 5 });
            Assert.Null(c.Language);
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Theory]
    [InlineData("analysis.m", "function y = f(x)\n% doc\ny = x;\nend\n", "MATLAB")]
    [InlineData("rules.pl", "likes(sam, pizza).\nhappy(X) :- likes(X, pizza).\n", "Prolog")]
    [InlineData("cpu.v", "`timescale 1ns/1ps\nmodule cpu(input clk);\nendmodule\n", "Verilog")]
    public void Classifier_uses_heuristics_end_to_end(string relPath, string content, string expected)
    {
        var classifier = new LanguageClassifier(DataStore.Load());
        var tmp = Path.Combine(Path.GetTempPath(), "ivy-heur-" + Guid.NewGuid().ToString("N") + Path.GetExtension(relPath));
        System.IO.File.WriteAllText(tmp, content);
        try
        {
            var c = classifier.Classify(new ScannedFile { RelativePath = relPath, FullPath = tmp, Length = content.Length });
            Assert.Equal(expected, c.Language);
        }
        finally { System.IO.File.Delete(tmp); }
    }
}
