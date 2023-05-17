namespace PSICover;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

// The CoverageAnalyzer for .Net
class Analyzer {
   public Analyzer (string dir, string runExe, params string[] modules) {
      Dir = dir; RunExe = runExe; Modules = modules.ToList ();
   }
   readonly string Dir;
   readonly string RunExe;
   readonly List<string> Modules;

   public void Run () {
      Modules.ForEach (MakeBackup);
      try {
         Modules.ForEach (Disassemble);
         Modules.ForEach (AddInstrumentation);
         Modules.ForEach (Assemble);
         RunCode ();
         GenerateOutputs ();
         GenerateSummary ();
      } finally {
         Modules.ForEach (RestoreBackup);
      }
   }

   // Make backups of the DLL and PDB files
   void MakeBackup (string module) {
      Console.WriteLine ("Making backups");
      Directory.CreateDirectory ($"{Dir}/Backups");
      File.Copy ($"{Dir}/{module}", $"{Dir}/Backups/{module}", true);
      var pdb = Path.ChangeExtension (module, ".pdb");
      File.Copy ($"{Dir}/{pdb}", $"{Dir}/Backups/{pdb}", true);
   }

   // Disassemble the DLL to create IL assembly files
   void Disassemble (string module) {
      Console.WriteLine ($"Disassembling {module}");
      var ildasmNew = $"{Dir}/ASMCore/ildasm.exe";
      var ildasmOld = $"{Dir}/ASMFramework/ildasm.exe";
      ExecProgram (ildasmOld, $"/LINENUM /TOKENS /out={Dir}/lines.asm {Dir}/{module}");
      ExecProgram (ildasmNew, $"/TOKENS /out={Dir}/nolines.asm {Dir}/{module}");

      string[] text1 = File.ReadAllLines ($"{Dir}/lines.asm").Where (a => !string.IsNullOrWhiteSpace (a)).ToArray ();
      List<string> text2 = File.ReadAllLines ($"{Dir}/nolines.asm").Where (a => !string.IsNullOrWhiteSpace (a)).ToList ();
      int n2 = 0;
      for (int n1 = 0; n1 < text1.Length; n1++) {
         var line = text1[n1].Trim ();
         if (line.StartsWith (".method /*")) {
            // Sync pointer n2 to the same method in the nolines.asm text
            for (; ; n2++)
               if (text2[n2] == text1[n1]) break;
            n2++;
            continue;
         }
         if (line.StartsWith (".line") && !line.StartsWith (".line 16707566")) {
            var match = rIL.Match (text1[n1 + 1].Trim ());
            if (match.Success) {
               SeekTo (match.Value);
               text2.Insert (n2, text1[n1]); n2++;
               continue;
            }
            match = rIL.Match (text1[n1 - 1].Trim ());
            if (match.Success) {
               SeekTo (match.Value);
               text2.Insert (n2 + 1, text1[n1]); n2++;
               continue;
            }
            throw new Exception ($"Could not match {line}");
         }
      }
      var asmFile = Path.ChangeExtension (module, ".original.asm");
      File.WriteAllLines ($"{Dir}/{asmFile}", text2.ToArray ());
      File.Delete ($"{Dir}/lines.asm");
      File.Delete ($"{Dir}/nolines.asm");

      // Helper .................................
      void SeekTo (string label) {
         for (; ; n2++) {
            var line2 = text2[n2].Trim ();
            if (line2.StartsWith (".method /*")) throw new Exception ("Found next method");
            var match1 = rIL.Match (line2);
            if (match1.Value == label) return;
         }
      }
   }
   static Regex rIL = new (@"^IL_[0-9a-fA-F][0-9a-fA-F][0-9a-fA-F][0-9a-fA-F]:", RegexOptions.Compiled);

   // Add the instrumentation (add a hit after each .line)
   void AddInstrumentation (string module) {
      module = Path.GetFileNameWithoutExtension (module);
      var infile = $"{Dir}/{module}.original.asm";
      var outfile = $"{Dir}/{module}.asm";
      string[] input = File.ReadAllLines (infile).Select (ModifyJumps).ToArray ();
      List<string> output = new ();
      for (int i = 0; i < input.Length; i++) {
         var s1 = input[i];
         output.Add (s1);
         if (s1.Trim ().StartsWith (".line ")) {
            var match = mRxLine.Match (s1);
            if (!match.Success) throw new Exception ("Unexpected .line directive");
            var s2 = input[i + 1];
            int colon = s2.IndexOf (':') + 1;
            if (colon != 0) {
               var groups = match.Groups;
               int nBlock = mBlocks.Count;
               mBlocks.Add (new Block (nBlock, int.Parse (groups[1].Value), int.Parse (groups[2].Value),
                  int.Parse (groups[3].Value), int.Parse (groups[4].Value), groups[5].Value));

               var label = s2[..colon];
               output.Add ($"{label} ldc.i4 {nBlock}");
               output.Add ("             call void [CoverLib]CoverLib.HitCounter::Hit(int32)");
               output.Add ("           " + s2[colon..]);
               i++;
            }
         } 
      }
      File.WriteAllLines (outfile, output);
   }
   static string ModifyJumps (string s) {
      if (!s.Contains (".s ")) return s;
      foreach (var jump in sJumps)
         s = s.Replace ($" {jump}.s ", $" {jump} ");
      return s;
   }
   static string[] sJumps = new[] {
      "leave", "br", "beq", "bge", "bge.un", "bgt", "bgt.un",
      "ble", "ble.un", "blt", "blt.un", "bne", "bne.un",
      "brfalse", "brnull", "brzero", "brtrue", "brinst" 
   };
   static Regex mRxLine = new Regex (@"\.line (\d+),(\d+) : (\d+),(\d+) '(.*)'");
   List<Block> mBlocks = new ();
   List<Tuple<string, int, int, double>> mCoverages = new ();

   // Re-assemble instrumented DLLs from the modified ASMs
   void Assemble (string module) {
      Console.WriteLine ($"Assembling {module}");
      File.Delete ($"{Dir}/{module}");
      var ilasm = $"{Dir}/ASMCore/ilasm.exe";
      var asmfile = $"{Dir}/{Path.GetFileNameWithoutExtension (module)}.asm";
      ExecProgram (ilasm, $"/QUIET /dll /PE64 /X64 {asmfile} /output={Dir}/{module}");
   }

   // Run the instrumented program to gather data (hits)
   void RunCode () {
      Console.WriteLine ("Running program");
      ExecProgram ($"{Dir}/{RunExe}", "");
   }

   // Generate output HTML (colored source code with hit / unhit areas marked)
   void GenerateOutputs () {
      ulong[] hits = File.ReadAllLines ($"{Dir}/hits.txt").Select (ulong.Parse).ToArray ();
      var files = mBlocks.Select (a => a.File).Distinct ().ToArray ();
      foreach (var file in files) {
         var blocks = mBlocks.Where (a => a.File == file)
                             .OrderBy (a => a.SPosition)
                             .ThenByDescending (a => a.EPosition)
                             .ToList ();
         for (int i = blocks.Count - 1; i > 0; i--)
            if (blocks[i - 1].Contains (blocks[i]))
               blocks.RemoveAt (i - 1);
         blocks.Reverse ();

         var code = File.ReadAllLines (file);
         for (int i = 0; i < code.Length; i++)
            code[i] = code[i].Replace ('<', '\u00ab').Replace ('>', '\u00bb');
         int hitCount = 0;
         foreach (var block in blocks) {
            bool hit = hits[block.Id] > 0;
            if (hit) hitCount++;
            string hitStyles = hit ? "hit tooltip" : "unhit";
            string tag = $"<span class=\"{hitStyles}\">";
            // Another solution, But this does not provide indentation.
            //if (block.ELine - block.SLine > 0) tag = tag.Replace (">", " style=\"white-space:pre-line\">");

            // Break multiple lines and attach span element to each line.
            var linesCount = block.ELine - block.SLine;
            if (linesCount > 0) {
               // Consider start and end line from the multiple lines.
               for (int i = 0; i <= linesCount; i++) {
                  int lineIndex = block.SLine + i;
                  var lineLength = code[lineIndex].Length;
                  var trimmedCode = code[lineIndex].Trim ();
                  var startIdx = code[lineIndex].IndexOf (trimmedCode);
                  code[lineIndex] = code[lineIndex].Insert (lineLength, "</span>");
                  if (hit) AddTooltip (lineIndex, lineLength);
                  code[lineIndex] = code[lineIndex].Insert (startIdx, tag);
               }
               continue;
            }
            code[block.ELine] = code[block.ELine].Insert (block.ECol, "</span>");
            if (hit) AddTooltip (block.ELine, block.ECol);
            code[block.SLine] = code[block.SLine].Insert (block.SCol, tag);

            void AddTooltip (int lineIdx, int pos) {
               // Add tooltip span before the closing tag.
               string tooltipTag = $"<span class=\"tooltip tooltiptext\"> Hit count: {hits[block.Id]} </span>";
               code[lineIdx] = code[lineIdx].Insert (pos, tooltipTag);
            }
         }
         var htmlDir = $"{Dir}/HTML";
         Directory.CreateDirectory (htmlDir);
         string htmlfile = Path.Combine (htmlDir, Path.GetFileNameWithoutExtension (file) + ".html");

         string html = $$"""
            <html><head><style>
            .hit { background-color:aqua; }
            .unhit { background-color:orange; }
            .tooltip { position: relative; display: inline; }
            .tooltiptext {
              border: 1px solid black;
              visibility: hidden;
              text-align: center;
              background-color:yellow;
              color:black;
              padding: 5px;
              width:150px;
              word-wrap:break-word;
              position: absolute;
              left: 110%;
              z-index:10;
            }
            .tooltip:hover .tooltiptext { visibility: visible; }
            </style></head>
            <body><pre>
            {{string.Join ("\r\n", code)}}
            </pre></body></html>
            """;
         html = html.Replace ("\u00ab", "&lt;").Replace ("\u00bb", "&gt;");
         File.WriteAllText (htmlfile, html);

         // Add the file coverage report to coverages collection.
         var hitPercent = Math.Round (100.0 * hitCount / blocks.Count, 1);
         mCoverages.Add (Tuple.Create (Path.GetFileName (file), blocks.Count, hitCount, hitPercent));
      }
      int cBlocks = mBlocks.Count, cHit = hits.Count (a => a > 0);
      double percent = Math.Round (100.0 * cHit / cBlocks, 1);
      Console.WriteLine ($"Coverage: {cHit}/{cBlocks}, {percent}%");
   }

   /// <summary>Generates HTML summary for the code coverages.</summary>
   void GenerateSummary () {
      string summaryContent = new Table (mCoverages).Generate ();
      var path = $"{Dir}/HTML/summary.html";
      File.WriteAllText (path, summaryContent);
      var proc = new Process {
         StartInfo = new ProcessStartInfo (path) { UseShellExecute = true }
      };
      proc.Start ();
   }

   // Restore the DLLs and PDBs from the backups
   void RestoreBackup (string module) {
      Console.WriteLine ("Restoring backups");
      Directory.CreateDirectory ($"{Dir}/Backups");
      File.Copy ($"{Dir}/Backups/{module}", $"{Dir}/{module}", true);
      var pdb = Path.ChangeExtension (module, ".pdb");
      File.Copy ($"{Dir}/Backups/{pdb}", $"{Dir}/{pdb}", true);
   }

   // Execute an external program, and wait for it to complete
   // (Also throws an exception if the external program returns a non-zero error code)
   static void ExecProgram (string name, string args) {
      var proc = Process.Start (name, args);
      proc.WaitForExit ();
      if (proc.ExitCode != 0)
         throw new Exception ($"Process {name} returned code {proc.ExitCode}");
   }
}

// Represents a basic code-coverage block (contiguous block of C# code)
class Block {
   public Block (int id, int sLine, int eLine, int sCol, int eCol, string file) {
      if (file == "") file = sLastFile;
      (Id, SLine, ELine, SCol, ECol, File) = (id, sLine - 1, eLine - 1, sCol - 1, eCol - 1, file);
      sLastFile = file;
   }

   public bool Contains (Block c) {
      if (File != c.File) return false;
      if (c.SPosition < SPosition) return false;
      if (c.EPosition > EPosition) return false;
      return true;
   }

   public override string ToString () 
      => $"{SLine},{ELine} : {SCol},{ECol} of {File}";

   public readonly int Id;
   public readonly int SLine, ELine, SCol, ECol;
   public int SPosition => SLine * 10000 + SCol;
   public int EPosition => ELine * 10000 + ECol;
   public readonly string File;
   static string sLastFile = "";
}

/// <summary>Creates table to summarize the coverage report</summary>
class Table {
   public Table (List<Tuple<string, int, int, double>> coverages) {
      Coverages = coverages;
   }

   /// <summary>Generates the summary.</summary>
   public string Generate () {
      StringBuilder table = new ();
      table.AppendLine ("<table width=\"50%\">");
      table.AppendLine ("<caption>Coverage summary</caption>");
      // Add table headers.
      table.AppendLine ("<tr>");
      for (int i = 0; i < headers.Count; i++) {
         var head = NewHeader (headers[i], i == 0);
         table.AppendLine (head);
      }
      table.AppendLine ("</tr>");
      // Add table rows.
      foreach (var coverage in Coverages.OrderBy (x => x.Item4))
         table.AppendLine (NewRow (coverage.Item1, coverage.Item2, coverage.Item3, coverage.Item4));

      return TableToHTML (table.ToString ());
   }

   string TableToHTML (string tablebody) {
      var content = $$""""
         <html>
         <head>
           <style>
           	table, th {
         		  border: 1px solid black;
         		  border-collapse: collapse;
         		  text-align:left;
           	}
           	td {
           		border: 1px solid black;
           		border-collapse: collapse;
           	}
           </style>
         </head>
         <body>
         	<div>
            {{tablebody}}
            </div>
         </body>
         </html>
         """";
      return content;
   }

   #region helpers
   string NewHeader (string heading, bool extraWidth = false) =>
    extraWidth ? $"<th width=\"40%\">{heading}</th>" : $" <th>{heading}</th>";

   string NewRow (string file, int blocks, int blockscovered, double coverPercent) =>
       $"<tr>\n<td>{file}</td>\n<td>{blocks}</td>\n<td>{blockscovered}</td>\n<td>{coverPercent}</td>\n</tr>";
   #endregion

   List<string> headers = new () { "Source files", "Blocks", "Blocks covered", "Coverage %" };
   readonly List<Tuple<string, int, int, double>> Coverages;
}

static class Program {
   public static void Main () {
      var analyzer = new Analyzer ("P:/Bin", "PSITest.exe", "parser.dll");
      analyzer.Run ();
   }
}
