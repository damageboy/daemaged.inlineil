//-----------------------------------------------------------------------------
// Inline IL tool. 
// Author Mike Stall (http://blogs.msdn.com/jmstall)
//
//  Allow inline IL in arbitrary .NET programs (such as C#). This is done
//  via stripping the IL snippets from the source and then injecting them
//  in during an Il roundtrip.
// 
// This is a purely academic tool and not intended for production use.
// See http://blogs.msdn.com/jmstall/archive/2005/02/21/377806.aspx for more details
// about this project.
//-----------------------------------------------------------------------------
#region Using directives

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Mono.Options;
using StringCollection = System.Collections.Specialized.StringCollection;
using System.Collections;

#endregion

namespace InlineIL
{
  // Various generic utility functions. Ideally, these would be things in the BCL.
  static class Util
  {
    // Run a process and block waiting for it to finish.
    // Output to our console.
    // Throws exception if process fails (has non-zero exception code).
    public static void Run(string cmd, string args)
    {
      var info = new ProcessStartInfo(cmd, args) {
        CreateNoWindow = false,
        UseShellExecute = false
      };
      //Console.WriteLine("Executing {0} {1}", cmd, args);
      var proc = Process.Start(info);
      proc.WaitForExit();

      if (proc.ExitCode != 0)
        throw new ArgumentException("Process '" + cmd + "' failed with exit code '" + proc.ExitCode + ")");
    }

    // Insert the snippet array into the source array at the given idx in the source array.
    // Returns the new array. After this returns:
    //    ReturnValue[idxSource] == arraySnippet[0];
    //    ReturnValue.Length == arraySource.Length + arraySnippet.Length;
    // Unfortunately, there doesn't appear to be a good array-insert function, so we write our own.
    public static string[] InsertArrayIntoArray(string[] arraySource, string[] arraySnippet, int idxSource)
    {
      Debug.Assert(arraySource != null);
      Debug.Assert(arraySnippet != null);
      Debug.Assert(idxSource >= 0);
      Debug.Assert(idxSource <= arraySource.Length);
      var temp = new string[arraySnippet.Length + arraySource.Length];
      Array.Copy(arraySource, 0, temp, 0, idxSource);
      Array.Copy(arraySnippet, 0, temp, idxSource, arraySnippet.Length);
      Array.Copy(arraySource, idxSource, temp, idxSource + arraySnippet.Length, arraySource.Length - idxSource);

      return temp;
    }
  }

  // Document to represent ILasm output.
  // This class encapsulates all knowledge of the textual representation of the IL. Thus this class
  // is the only place that has to be changed if ilasm/ildasm change.

  // Class to represent a snippet of Inline IL

  class Program
  {

      // Searches the given source file for inline IL snippets.
    static List<InlineILSnippet> FindInlineILSnippets(ILDocument doc)
    {
      var snippets = new List<InlineILSnippet>();
      var sourceFiles = doc.GetSourceFiles();
              
      foreach (var f in sourceFiles) {
        var lang = GetLanguageForFile(f);

        // An IL snippet begins at the first line prefixed with the startMarker
        // and goes until the first endMarker after that.
        var stStartMarker = lang.StartMarker.ToLower();
        var stEndMarker = lang.EndMarker.ToLower();

        TextReader reader = new StreamReader(new FileStream(f, FileMode.Open));

        StringCollection list = null;

        var idxStartLine = 0; // 0 means we're not tracking a snippet.
        var idxLine = 0; // current line into source file.
        string line;
        while ((line = reader.ReadLine()) != null) {
          var lineLowercase = line.ToLower();
          idxLine++;
          if (idxStartLine != 0) {
            if (lineLowercase == stEndMarker) {
              // We found the end of the IL snippet.
              var idxEndLine = idxLine - 1; // end line was the previous line
              var snippet = new InlineILSnippet(f, idxStartLine, idxEndLine, list);
              snippets.Add(snippet);

              idxStartLine = 0; // reset tracking the IL snippet.
              list = null;
            }
            else {
              list.Add(line);
            }
          }

          if (!lineLowercase.StartsWith(stStartMarker)) continue;
          // We found the start of an IL snippet. The actual snippet will start at the next line.
          list = new StringCollection();
          idxStartLine = idxLine + 1;
        }

        // If we got to the end of the file and are still tracking, then we have an unterminated inline IL segment.
        if (idxStartLine != 0) {
          throw new ArgumentException(string.Format("Unterminated Inline IL segment in file '{0}' starting with '{1}' at line '{2}'. Expecting to find closing '{3}'",
                                                    f, stStartMarker, idxStartLine, stEndMarker));
        }
      }
      return snippets;
    }
    #region SDK Property

    public static string SdkDir { get; private set; }


    static void InitSdkDir()
    {

      // Need to determine Sdk path. The vcvars batch script will set the env var FrameworkSDKDir            
      SdkDir = Environment.GetEnvironmentVariable("FrameworkSDKDir");
      if (!String.IsNullOrEmpty(SdkDir))
        SdkDir = Path.Combine(SdkDir, "Bin");

      if (SdkDir == null) {
        var sdkRegKey =
          String.Format(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\Windows\v7.0A\WinSDK-NetFx{0}{1}Tools",
                        Environment.Version.Major, Environment.Version.Minor);

        var sdkRegPath = (string) Registry.GetValue(sdkRegKey, "InstallationFolder", null);
        if (Directory.Exists(sdkRegPath))
          SdkDir = sdkRegPath;
      }
      // If we can't find it, use the default location for V2.0 installed with VS.
      if (SdkDir == null)        
        SdkDir = @"C:\Program Files\Microsoft Visual Studio 8\SDK\v2.0";

    }
    #endregion // SDK Property

    // Main entry point.
    static int Main(string[] args)
    {
      string outputType = null;
      var showHelp = false;
      string outputFile = null;
      string inputFile = null;
      var verify = false;
      var verbose = false;
      string keyFile = null;
      var o = new OptionSet {
        {"dll", v => outputType = "DLL"},
        {"exe", v => outputType = "EXE"},
        {"k|key=", v => keyFile = v},
        {"c|verify", v => verify = v != null },
        {"v|verbose", v => verbose = v != null },
        {"i|input=", v => inputFile = v},
        {"o|output=", v => outputFile = v},
        {"h|?|help", "show this help message", v => showHelp = v != null},
      };

      var extra = o.Parse(args);

      if (showHelp) {
        ShowHelp(o);
        return 0;
      }              

      if (extra.Count != 0)
      {
        ShowHelp(o);
        return 1;
      }

      InitSdkDir();
                  
      try
      {
        var doc = new ILDocument(inputFile);

        var snippets = FindInlineILSnippets(doc);

        // Re-inject the snippets.
        foreach (var s in snippets) {
          if (verbose) {
            Console.WriteLine("Found:" + s);
            foreach (var x in s.Lines)
              Console.WriteLine("   :" + x);
          }

          s.InsertLocation = doc.FindSnippetLocation(s);
        }

        snippets.Sort((x, y) => y.InsertLocation - x.InsertLocation);
        foreach (var s in snippets)
          doc.Lines = Util.InsertArrayIntoArray(doc.Lines, s.Lines, s.InsertLocation);
        // Now re-emit the new IL.
        doc.EmitToFile(outputFile, outputType, keyFile);

        // Since they're doing direct IL manipulation, we really should run peverify on the output.         
        if (verify) {
          Console.WriteLine("Running PEVerify on '{0}'.", outputFile);
          Util.Run(Path.Combine(SdkDir + "PEverify.exe"), outputFile);
          Console.WriteLine("PEVerify passed!");
        }
      }
      catch (Exception e)
      {
        Console.WriteLine("Error:" + e.Message);
        return 1;
      }
      return 0;
    }

    private static void ShowHelp(OptionSet optionSet)
    {
      Console.WriteLine("Inline IL post-compiler tool");
      Console.WriteLine("Based (or rather blatebtly copied from): http://blogs.msdn.com/jmstall/archive/2005/02/21/377806.aspx");
      Console.WriteLine();
      optionSet.WriteOptionDescriptions(Console.Out);

    }

    // Get a language service for the source file.
    static ILanguage GetLanguageForFile(string pathSourceFile)
    {
      var ext = Path.GetExtension(pathSourceFile);
      if (String.Compare(ext, ".vb", true) == 0)
        return new VisualBasicLanguage();
      if (String.Compare(ext, ".cs", true) == 0)
        return new CSharpLanguage();
      Console.WriteLine("** Can't identify language for '{0}', using C#.", pathSourceFile);
      return new CSharpLanguage();
    }

  }
}