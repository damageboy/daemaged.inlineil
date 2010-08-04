using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InlineIL
{
  // Abstraction for different languages.
  interface ILanguage
  {
    // Source line that indicates an inline IL snippet is starting. 
    string StartMarker { get; }

    // Source line that ends an inline IL snippet. Must match with StartMarker.
    string EndMarker { get; }
  }

  // Language service for the VB.Net compiler.
  class VisualBasicLanguage : ILanguage
  {
    public string StartMarker
    {
      get { return "#If IL Then"; }
    }
    public string EndMarker
    {
      get { return "#End If"; }
    }
  }

  // Language service for the C# compiler.
  class CSharpLanguage : ILanguage
  {
    public string StartMarker
    {
      get { return "#if IL"; }
    }
    public string EndMarker
    {
      get { return "#endif"; }
    }
  }
}
