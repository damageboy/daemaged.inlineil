using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace InlineIL
{
  class ILDocument
  {
    // We represent the ILasm document as a set of lines. This makes it easier to manipulate it
    // (particularly to inject snippets)

    // Create a new ildocument for the given module.
    public ILDocument(string pathModule)
    {
      // ILDasm the file to produce a textual IL file.
      //   /linenum  tells ildasm to preserve line-number information. This is needed so that we don't lose
      // the source-info when we round-trip the il.

      var pathTempIl = Path.GetTempFileName();

      // We need to invoke ildasm, which is in the sdk. 
      var pathIldasm = Program.SdkDir + "ildasm.exe";

      // We'd like to use File.Exists to make sure ildasm is available, but that function appears broken.
      // It doesn't allow spaces in the filenam, even if quoted. Perhaps just a beta 1 bug.
      
      Util.Run(pathIldasm, "\"" + pathModule + "\" /linenum /text /nobar /out=\"" + pathTempIl + "\"");


      // Now read the temporary file into a string list.
      var temp = new StringCollection();
      using (TextReader reader = new StreamReader(new FileStream(pathTempIl, FileMode.Open)))
      {
        string line;
        while ((line = reader.ReadLine()) != null) {
          // Remove .maxstack since the inline IL will very likely increase stack size.
          if (line.Trim().StartsWith(".maxstack"))
            line = "// removed .maxstack declaration";

          temp.Add(line);
        }
      }
      Lines = new string[temp.Count];
      temp.CopyTo(Lines, 0);
    }

    public string[] Lines { get; set; }

    // Save the IL document back out to a file.
    public void EmitToFile(string pathOutputModule, string outputType)
    {
      var pathTempIl = Path.GetTempFileName();

      // Dump to file.
      using (TextWriter writer = new StreamWriter(new FileStream(pathTempIl, FileMode.Create)))
      {
        foreach (var line in Lines)
        {
          var x = line;
          // ilasm thinks different casings are different source files.
          if (line.Trim().StartsWith(".line"))
            x = line.ToLower();
          writer.WriteLine(x);
        }
      }

      var pathIlasm = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "ilasm.exe");

      // Run ilasm to re-emit it. It will look something like this:
      // 	ilasm t.il /output=t2.exe /optimize /debug 
      //   /optimize tells ilasm to convert long instructions to short forms (eg "ldarg 0 --> ldarg.0")
      //   /debug (instead of /debug=impl) tells the runtime to use explicit sequence points 
      // (which are necessary to single-step the IL instructions that we're inlining)
      Util.Run(pathIlasm, string.Format("\"{0}\" /output=\"{1}\" /optimize /debug /{2} /nologo /quiet", pathTempIl, pathOutputModule, outputType));
    }

    // Insert a snippet of IL into the document.
    public int FindSnippetLocation(InlineILSnippet snippet)
    {
      var snippetStart = snippet.StartLine;
      var snippetEnd = snippet.EndLine;

      // We need to find where to place the IL snippet in our ildasm output.
      // If the IL snippet is at source line (f, g) (inclusive), the ildasm should contain 
      // consecutive .line directives such '.line x' and '.line y' such that (x > f) && (g > y).
      // Once we find such a pair, we can inject the ilasm snippet into the ilasm document at
      // the line before the one containing '.line y'.            

      // intentionally pick MaxValue so that (idxLast < idxStartLine) is false until we initialize idxLast
      var lastKnownLine = int.MaxValue;

      var idxInsertAt = -1;
      var idxIlasmLine = 1; // source files are 1-based.
      var fileName = String.Empty;
      foreach (var line in Lines) {
        //Console.WriteLine(line);
        Match m;        
        if ((m = _lineFileRegex.Match(line)).Success) {
          fileName = m.Groups["filename"].Value;
          //Console.WriteLine("Switching to {0} @ line {1}", fileName, idxIlasmLine);
        }

        if (fileName != snippet.Sourcefile) {
          idxIlasmLine++;
          continue;
        }

        var idxCurrent = GetLineMarker(line);
        if (idxCurrent != 0) {
          if ((lastKnownLine < snippetStart) && (snippetEnd < idxCurrent)) {
            // What if there are multiple such values of (x,y)? 
            // Probably should inject at each spot. - which means we may need a while-loop here instead of foreach
            if (idxInsertAt != -1)
              throw new Exception("ILAsm snippet needs to be inserted at multiple spots.");

            // Found snippets location! Insert before ilasm source line idxIlasmLine 
            // (which is index idxIlasmLine-1, since the array is 0-based)
            //Console.WriteLine("Found possible snippet {0}:{1}-{2} location @ line {3}",
            //  snippet.Sourcefile, snippet.StartLine, snippet.EndLine, idxIlasmLine);
            idxInsertAt = idxIlasmLine - 1;
          }

          lastKnownLine = idxCurrent;
        }

        idxIlasmLine++;
      }

      if (idxInsertAt == -1)
      {
        throw new ArgumentException("Can't find where to place " + snippet);
      }
      //Console.WriteLine("Inserting to .il file @ {0}", idxInsertAt);
      //Lines = Util.InsertArrayIntoArray(Lines, snippet.Lines, idxInsertAt);
      return idxInsertAt;
    }

    #region IL Text parsing Utility
    // Is this a line number marker? ".line #,"
    // Returns 0 if this isn't a line marker.
    // Note that this does not work with multiple source files.
    private static readonly Regex _reLine = new Regex(@"\s*\.line (\d+),");
    private static readonly Regex _lineFileRegex = new Regex(@"line \d+,\d+ : \d+,\d+ '(?'filename'[^']+)'");

    static int GetLineMarker(string line)
    {
      var i = 0;
      var m = _reLine.Match(line);
      if (m.Success)
      {
        var val = m.Groups[1].Value;
        if (int.TryParse(val, out i))
          // Protect for hidden lines (0xFeeFee) 
          // http://blogs.msdn.com/b/jmstall/archive/2005/06/19/feefee-sequencepoints.aspx
          return i == 0xFEEFEE ? 0 : i;
      }
      return 0;
    }

    // Get a ILasm string for a sequence point marker. Eg, should look something like:
    //     .line 7,7 : 2,6 'c:\\temp\\t.cs'
    public static string CreateILSequenceMarker(string pathSourceFile, int idxLineStart, int idxLineEnd, int idxColStart, int idxColEnd)
    {
      var pathEscapedSourceFile = pathSourceFile.Replace(@"\", @"\\");
      return ".line " + idxLineStart + "," + idxLineEnd + " : " + idxColStart + "," + idxColEnd + " '" + pathEscapedSourceFile + "'";
    }

    // Given a line of IL, determine if it's a statement.
    // We can't add markers to none-statement lines.
    static public bool IsStatement(string line)
    {
      // Parsing the ilasm text lines seems very hacky here. This is one place it would be great if we had
      // a codedom for ilasm.
      // It would be best to run this through a compiled regular expression instead of the adhoc string operations.
      var t = line.Trim();

      // skip blank lines and comments.
      if (t.Length <= 1) return false;
      if (t.StartsWith("//")) return false;

      // The '.' includes all sorts of metacommands that we wan't to skip.
      var ch = t[0];
      if (ch == '.' || ch == '{' || ch == '}') return false;

      return !t.StartsWith("catch") && !t.StartsWith("filter");
    }

    #endregion IL Text parsing Utility

    //.line 27,27 : 5,44 'c:\Path\To\File.cs'
    //.line 27,27 : 5,44 ''
    

    public IList<string> GetSourceFiles()
    {
      return Lines.Select(l => _lineFileRegex.Match(l)).Where(m => m.Success).Select(m => m.Groups["filename"].Value).Distinct().ToList();
    }
  }
}