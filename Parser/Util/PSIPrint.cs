// ⓅⓈⒾ  ●  Pascal Language System  ●  Academy'23
// PSIPrint.cs ~ Prints a PSI syntax tree in Pascal format
// ─────────────────────────────────────────────────────────────────────────────
namespace PSI;

public class PSIPrint : Visitor<StringBuilder> {
   public override StringBuilder Visit (NProgram p) {
      Write ($"program {p.Name}; ");
      Visit (p.Block);
      return Write (".");
   }

   public override StringBuilder Visit (NBlock b) 
      => Visit (b.Decls, b.Body);

   public override StringBuilder Visit (NDeclarations d) {
      if (d.Vars.Length > 0) {
         NWrite ("var"); N++;
         foreach (var g in d.Vars.GroupBy (a => a.Type))
            NWrite ($"{g.Select (a => a.Name).ToCSV ()} : {g.Key};");
         N--;
      }
      if (d.Funcs.Length > 0) {
         foreach (var f in d.Funcs) Visit (f);
      }
      return S;
   }

   public override StringBuilder Visit (NVarDecl d)
      => NWrite ($"{d.Name} : {d.Type}");

   public override StringBuilder Visit (NFnDecl f) {
      if (f.Type != NType.Unknown)
         NWrite ("function ");
      else NWrite ("procedure ");
      N++;
      Write (f.Name.Text);
      Write ("(");
      string dec = "";
      foreach (var g in f.Params.GroupBy (a => a.Type))
         dec += $"{g.Select (a => a.Name).ToCSV ()} : {g.Key},";
      if (dec.EndsWith (",")) dec = dec[..^1];
      Write (dec);
      Write (")");
      if (f.Type != NType.Unknown)
         Write ($": {f.Type}");
      Write (";");
      // Print block
      Visit (f.Block);
      Write (";");
      N--;
      return S;
   }

   public override StringBuilder Visit (NCompoundStmt b) {
      NWrite ("begin"); N++;  Visit (b.Stmts); N--; return NWrite ("end"); 
   }

   public override StringBuilder Visit (NAssignStmt a) {
      NWrite ($"{a.Name} := "); a.Expr.Accept (this); return Write (";");
   }

   public override StringBuilder Visit (NWriteStmt w) {
      NWrite (w.NewLine ? "WriteLn (" : "Write (");
      for (int i = 0; i < w.Exprs.Length; i++) {
         if (i > 0) Write (", ");
         w.Exprs[i].Accept (this);
      }
      return Write (");");
   }

   public override StringBuilder Visit (NIfStmt i) {
      NWrite ("if ");
      i.Expr.Accept (this);
      Write (" then");
      N++;
      i.IfStmt.Accept (this);
      N--;
      if (i.ElseStmt != null) {
         NWrite ("else");
         N++;
         i.ElseStmt.Accept (this);
         N--;
      }
      return S;
   }

   public override StringBuilder Visit (NFnCallStmt f) {
      NWrite (f.Name.Text + "(");
      for (int i = 0; i < f.Exprs.Length; i++) {
         if (i > 0) Write (",");
         f.Exprs[i].Accept (this);
      }
      return Write (");");
   }

   public override StringBuilder Visit (NReadStmt r) {
      NWrite ("read (");
      for(int i =0; i < r.Tokens.Length; i++) {
         if (i > 0) Write (",");
         Write ($"{r.Tokens[i].Text}");
      }
     return Write (")");
   }
   public override StringBuilder Visit (NForStmt f) {
      var a = f.Assignment;
      NWrite ("for ");
      Write ($"{a.Name} := "); a.Expr.Accept (this);
      // determine to or downto
      Write (" to ");
      f.Expr.Accept (this);
      Write (" do ");
      Write ("begin");
      f.Stmt.ForEach (x => x.Accept (this)); // More test needed.
      NWrite ("end;");
      return S;
   }

   public override StringBuilder Visit (NLiteral t)
      => Write (t.Value.ToString ());

   public override StringBuilder Visit (NIdentifier d)
      => Write (d.Name.Text);

   public override StringBuilder Visit (NUnary u) {
      Write (u.Op.Text); return u.Expr.Accept (this);
   }

   public override StringBuilder Visit (NBinary b) {
      Write ("("); b.Left.Accept (this); Write ($" {b.Op.Text} ");
      b.Right.Accept (this); return Write (")");
   }

   public override StringBuilder Visit (NFnCall f) {
      Write ($"{f.Name} (");
      for (int i = 0; i < f.Params.Length; i++) {
         if (i > 0) Write (", "); f.Params[i].Accept (this);
      }
      return Write (")");
   }

   StringBuilder Visit (params Node[] nodes) {
      nodes.ForEach (a => a.Accept (this));
      return S;
   }

   // Writes in a new line
   StringBuilder NWrite (string txt) 
      => Write ($"\n{new string (' ', N * 3)}{txt}");
   int N;   // Indent level

   // Continue writing on the same line
   StringBuilder Write (string txt) {
      Console.Write (txt);
      S.Append (txt);
      return S;
   }

   readonly StringBuilder S = new ();
}