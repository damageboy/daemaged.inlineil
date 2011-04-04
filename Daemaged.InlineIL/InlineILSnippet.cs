using System;
using System.Collections.Specialized;
using System.Diagnostics;

namespace InlineIL
{
  class InlineILSnippet
  {
    // Create an IL snippet that matches the given range (startLine to endLine, inclusive) in the
    // given source file. 
    // Although we could compute the lines collection from the other 3 parameters, we pass it in for perf reasons
    // since we already have it available.
    public InlineILSnippet(string pathSourceFile, int idxStartLine, int idxEndLine, StringCollection lines)
    {
      Sourcefile = pathSourceFile;
      StartLine = idxStartLine;
      EndLine = idxEndLine;

      // This assert would be false if the incoming lines collection has been preprocessed (Eg, if the caller
      // already inject .line directives to map back to the source).
      Debug.Assert(idxEndLine - idxStartLine + 1 == lines.Count);

      // Marshal into an array. Since we're already copying, we'll also inject the sequence point info.
      Lines = new string[(lines.Count * 2)+1];
      Lines[0] = string.Format("// Snippet from {0}:{1}", pathSourceFile, idxStartLine);
      

      for (var i = 0; i < lines.Count; i++)
      {
        var idxSourceLine = idxStartLine + i;

        // ILAsm only lets us add sequence points to statements.
        var sequenceMarker = ILDocument.IsStatement(lines[i]) ? 
          ILDocument.CreateILSequenceMarker(pathSourceFile, idxSourceLine, idxSourceLine, 1, lines[i].Length + 1) : 
          "// skip sequence marker";

        Lines[2 * i + 1] = sequenceMarker;
        Lines[2 * i + 2] = lines[i];
      }
    }

    public override string ToString()
    { return "Snippet in file '" + Sourcefile + "' at range (" + StartLine + "," + EndLine + ")"; }


    #region Properties
    // First line of the IL snippet within the source document.
    public int StartLine { get; private set; }

    // Last line (inclusive) of the IL snippet in the source document.
    // Total number of lines in IL snippet is (EndLine - StartLine + 1)
    public int EndLine { get; private set; }

    // Path to source file that IL snippet originally occured in.
    // This can be used to generate sequence points from the snippet back to the original source file.
    public string Sourcefile { get; private set; }

    public string[] Lines { get; private set; }
    public int InsertLocation { get; set; }

    #endregion Properties

  }
}