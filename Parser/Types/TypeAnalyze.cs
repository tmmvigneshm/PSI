// ⓅⓈⒾ  ●  Pascal Language System  ●  Academy'23
// TypeAnalyze.cs ~ Type checking, type coercion
// ─────────────────────────────────────────────────────────────────────────────
namespace PSI;
using static NType;
using static Token.E;

public class TypeAnalyze : Visitor<NType> {
   public TypeAnalyze () {
      mSymbols = SymTable.Root;
   }
   SymTable mSymbols;

   #region Declarations ------------------------------------
   public override NType Visit (NProgram p) 
      => Visit (p.Block);
   
   public override NType Visit (NBlock b) {
      mSymbols = new SymTable { Parent = mSymbols };
      Visit (b.Declarations); Visit (b.Body);
      mSymbols = mSymbols.Parent;
      return Void;
   }

   public override NType Visit (NDeclarations d) {
      Visit (d.Consts);  Visit (d.Vars); return Visit (d.Funcs);
   }

   public override NType Visit (NConstDecl c) {
      mSymbols.Consts.Add (c);
      ((NLiteral)c.Expr).Accept (this);
      return c.Expr.Type;
   }

   public override NType Visit (NVarDecl d) {
      // Check multiple varialbes declared in the same scope.
      var node = mSymbols.Find (d.Name.Text);
      if (node != null) {
         string? txt = node is NConstDecl ? $"{((NConstDecl)node).Expr.Type}" : $"{((NVarDecl)node).Type}";
         throw new ParseException (d.Name, $"A variable named '{d.Name.Text}' of type '{txt}' is already defined in this scope");
      }
      mSymbols.Vars.Add (d);
      return d.Type;
   }

   public override NType Visit (NFnDecl f) {
      var node = mSymbols.Find (f.Name.Text);
      if (node is NFnDecl)
         throw new ParseException (f.Name, $"A function called '{f.Name.Text}' is already defined in this scope");
      if (node != null)
         throw new ParseException (f.Name, $"Invalid function name. A variable with name '{f.Name.Text}' already defined in this scope");
      mSymbols.Funcs.Add (f);
      return f.Return;
   }
   #endregion

   #region Statements --------------------------------------
   public override NType Visit (NCompoundStmt b)
      => Visit (b.Stmts);

   public override NType Visit (NAssignStmt a) {
      if (mSymbols.Find (a.Name.Text) is not NVarDecl v)
         throw new ParseException (a.Name, "Unknown variable");
      a.Expr.Accept (this);
      a.Expr = AddTypeCast (a.Name, a.Expr, v.Type);
      return v.Type;
   }
   
   NExpr AddTypeCast (Token token, NExpr source, NType target) {
      if (source.Type == target) return source;
      bool valid = (source.Type, target) switch {
         (Int, Real) or (Char, Int) or (Char, String) => true,
         _ => false
      };
      if (!valid) throw new ParseException (token, "Invalid type");
      return new NTypeCast (source) { Type = target };
   }

   public override NType Visit (NWriteStmt w)
      => Visit (w.Exprs);

   public override NType Visit (NIfStmt f) {
      f.Condition.Accept (this);
      f.IfPart.Accept (this); f.ElsePart?.Accept (this);
      return Void;
   }

   public override NType Visit (NForStmt f) {
      f.Start.Accept (this); f.End.Accept (this); f.Body.Accept (this);
      return Void;
   }

   public override NType Visit (NReadStmt r) {
      throw new NotImplementedException ();
   }

   public override NType Visit (NWhileStmt w) {
      w.Condition.Accept (this); w.Body.Accept (this);
      return Void; 
   }

   public override NType Visit (NRepeatStmt r) {
      Visit (r.Stmts); r.Condition.Accept (this);
      return Void;
   }

   public override NType Visit (NCallStmt c) {
      if (mSymbols.Find (c.Name.Text) is NFnDecl fn)
         return AddFnParamTypes (fn, c.Params, c.Name);

      throw new ParseException (c.Name, $"Function {c.Name.Text} does not exist in the current context");
   }
   #endregion

   #region Expression --------------------------------------
   public override NType Visit (NLiteral t) {
      t.Type = t.Value.Kind switch {
         L_INTEGER => Int, L_REAL => Real, L_BOOLEAN => Bool, L_STRING => String,
         L_CHAR => Char, _ => Error,
      };
      return t.Type;
   }

   public override NType Visit (NUnary u) 
      => u.Expr.Accept (this);

   public override NType Visit (NBinary bin) {
      NType a = bin.Left.Accept (this), b = bin.Right.Accept (this);
      bin.Type = (bin.Op.Kind, a, b) switch {
         (ADD or SUB or MUL or DIV, Int or Real, Int or Real) when a == b => a,
         (ADD or SUB or MUL or DIV, Int or Real, Int or Real) => Real,
         (MOD, Int, Int) => Int,
         (ADD, String, _) => String, 
         (ADD, _, String) => String,
         (LT or LEQ or GT or GEQ, Int or Real, Int or Real) => Bool,
         (LT or LEQ or GT or GEQ, Int or Real or String or Char, Int or Real or String or Char) when a == b => Bool,
         (EQ or NEQ, _, _) when a == b => Bool,
         (EQ or NEQ, Int or Real, Int or Real) => Bool,
         (AND or OR, Int or Bool, Int or Bool) when a == b => a,
         _ => Error,
      };
      if (bin.Type == Error)
         throw new ParseException (bin.Op, "Invalid operands");
      var (acast, bcast) = (bin.Op.Kind, a, b) switch {
         (_, Int, Real) => (Real, Void),
         (_, Real, Int) => (Void, Real), 
         (_, String, not String) => (Void, String),
         (_, not String, String) => (String, Void),
         _ => (Void, Void)
      };
      if (acast != Void) bin.Left = new NTypeCast (bin.Left) { Type = acast };
      if (bcast != Void) bin.Right = new NTypeCast (bin.Right) { Type = bcast };
      return bin.Type;
   }

   public override NType Visit (NIdentifier d) {
      if (mSymbols.Find (d.Name.Text) is NVarDecl v) 
         return d.Type = v.Type;
      if (mSymbols.Find (d.Name.Text) is NConstDecl c) {
         ((NLiteral)c.Expr).Accept (this);
         return d.Type = c.Expr.Type;
      }
      throw new ParseException (d.Name, "Unknown variable");
   }

   // Check for types.
   // If types match fine, otherwise do typecase whereever possible.
   // Helper function, source type and target type is it possible to add a cast.
   // Throw exception if not parameters match.
   // Return type assignment already handled.
   public override NType Visit (NFnCall f) {
      if (mSymbols.Find (f.Name.Text) is NFnDecl fd) {
         return f.Type = AddFnParamTypes (fd, f.Params, f.Name);
      }
      throw new ParseException (f.Name, $"Function {f.Name.Text} does not exist in the current context");
   }

   // TODO: Need to support method overload.
   // e.g. Source:Max(int,int) to Target:Max(double,double)
   NType AddFnParamTypes (NFnDecl fnDecl, NExpr[] exprs, Token token) {
      if (fnDecl.Params.Length != exprs.Length) {
         throw new ParseException (token, $"No overload for function '{token.Text}' takes {exprs.Length} aruguments");
      }
      for (int i = 0; i < exprs.Length; i++) {
         var srcType = exprs[i].Accept (this);
         var target = fnDecl.Params[i];
         // Type cast should be possible in function call, for int to real.
         var iConversionPossible = srcType == Int && target.Type == Real;
         if (!iConversionPossible && srcType != target.Type) {
            throw new ParseException (token, $"Argument{i + 1}: Cannot convert from '{srcType}' to '{target.Type}'");
         }
         exprs[i] = AddTypeCast (token, exprs[i], target.Type);
      }
      return fnDecl.Return;
   }

   public override NType Visit (NTypeCast c) {
      c.Expr.Accept (this); return c.Type;
   }
   #endregion

   NType Visit (IEnumerable<Node>? nodes) {
      if (nodes == null) return NType.Void;
      foreach (var node in nodes) node.Accept (this);
      return NType.Void;
   }
}
