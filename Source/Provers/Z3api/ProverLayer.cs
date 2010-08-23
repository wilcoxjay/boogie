using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Diagnostics;
using Microsoft.Contracts;
using Microsoft.Boogie.AbstractInterpretation;
using Microsoft.Boogie;
using Microsoft.Boogie.Z3;
using Microsoft.Z3;
using Microsoft.Boogie.VCExprAST;

using TypeAst = System.IntPtr;
using TermAst = System.IntPtr;
using ConstDeclAst = System.IntPtr;
using ConstAst = System.IntPtr;
using Value = System.IntPtr;
using PatternAst = System.IntPtr;

namespace Microsoft.Boogie.Z3
{
    public class Z3apiProcessTheoremProver : Z3ProcessTheoremProver
    {
        public Z3apiProcessTheoremProver(VCExpressionGenerator gen,
                                         DeclFreeProverContext ctx,
                                         Z3InstanceOptions opts)
            : base(gen, ctx, opts)
        {
            this.z3ContextIsUsed = false;
        }

        private bool z3ContextIsUsed;

        public void PushAxiom(VCExpr axiom)
        {
            Z3SafeContext cm = ((Z3apiProverContext)ctx).cm;
            cm.CreateBacktrackPoint();
            LineariserOptions linOptions = new Z3LineariserOptions(false, (Z3InstanceOptions)this.options, new List<VCExprVar>());
            cm.AddAxiom((VCExpr)axiom, linOptions);
        }
        private void PushConjecture(VCExpr conjecture)
        {
            Z3SafeContext cm = ((Z3apiProverContext)ctx).cm;
            cm.CreateBacktrackPoint();
            LineariserOptions linOptions = new Z3LineariserOptions(false, (Z3InstanceOptions)this.options, new List<VCExprVar>());
            cm.AddConjecture((VCExpr)conjecture, linOptions);
        }

        public void PrepareCheck(string descriptiveName, VCExpr vc)
        {
            PushAxiom(ctx.Axioms);
            PushConjecture(vc);
        }

        public void BeginPreparedCheck()
        {
            Z3SafeContext cm = ((Z3apiProverContext)ctx).cm;
            outcome = Outcome.Undetermined;
            outcome = cm.Check(out z3LabelModels);
        }

        public override void BeginCheck(string descriptiveName, VCExpr vc, ErrorHandler handler)
        {
            LineariserOptions linOptions = new Z3LineariserOptions(false, (Z3InstanceOptions)this.options, new List<VCExprVar>());
            Z3SafeContext cm = ((Z3apiProverContext)ctx).cm;
            if (z3ContextIsUsed)
            {
                cm.Backtrack();
            }
            else
            {
                cm.AddAxiom((VCExpr)ctx.Axioms, linOptions);
                z3ContextIsUsed = true;
            }

            cm.CreateBacktrackPoint();
            cm.AddConjecture((VCExpr)vc, linOptions);

            BeginPreparedCheck();
        }

        private Outcome outcome;
        private List<Z3ErrorModelAndLabels> z3LabelModels = new List<Z3ErrorModelAndLabels>();

        [NoDefaultContract]
        public override Outcome CheckOutcome(ErrorHandler handler)
        {
            if (outcome == Outcome.Invalid)
            {
                foreach (Z3ErrorModelAndLabels z3LabelModel in z3LabelModels)
                {
                    List<string> unprefixedLabels = RemovePrefixes(z3LabelModel.RelevantLabels);
                    handler.OnModel(unprefixedLabels, z3LabelModel.ErrorModel);
                }
            }
            return outcome;
        }

        public void CreateBacktrackPoint()
        {
            Z3SafeContext cm = ((Z3apiProverContext)ctx).cm;
            cm.CreateBacktrackPoint();
        }
        override public void Pop()
        {
            Z3SafeContext cm = ((Z3apiProverContext)ctx).cm;
            cm.Backtrack();
        }

        private List<string> RemovePrefixes(List<string> labels)
        {
            List<string> result = new List<string>();
            foreach (string label in labels)
            {
                if (label.StartsWith("+"))
                {
                    result.Add(label.Substring(1));
                }
                else if (label.StartsWith("|"))
                {
                    result.Add(label.Substring(1));
                }
                else if (label.StartsWith("@"))
                {
                    result.Add(label.Substring(1));
                }
                else
                    throw new Exception("Unknown prefix in label " + label);
            }
            return result;
        }
    }

    public class Z3apiProverContext : DeclFreeProverContext
    {
        public Z3SafeContext cm;
        
        public Z3apiProverContext(VCExpressionGenerator gen, 
                                  VCGenerationOptions genOptions, 
                                  Z3InstanceOptions opts)
            : base(gen, genOptions)
        {
            Z3Config config = BuildConfig(opts.Timeout, true);
            this.cm = new Z3SafeContext(config, gen);
        }
        private static Z3Config BuildConfig(int timeout, bool nativeBv)
        {
            Z3Config config = new Z3Config();
            config.SetModelCompletion(false);
            config.SetModel(true);

            if (0 <= CommandLineOptions.Clo.ProverCCLimit)
            {
                config.SetCounterExample(CommandLineOptions.Clo.ProverCCLimit);
            }

            if (0 <= timeout)
            {
                config.SetSoftTimeout(timeout.ToString());
            }

            config.SetTypeCheck(true);
            return config;
        }

        public override void DeclareType(TypeCtorDecl t, string attributes)
        {
            base.DeclareType(t, attributes);
            cm.DeclareType(t.Name);
        }

        public override void DeclareConstant(Constant c, bool uniq, string attributes)
        {
            base.DeclareConstant(c, uniq, attributes);
            cm.DeclareConstant(c.Name, c.TypedIdent.Type);
        }

        public override void DeclareFunction(Function f, string attributes)
        {
            base.DeclareFunction(f, attributes);
            List<Type> domain = new List<Type>();
            foreach (Variable v in f.InParams)
            {
                domain.Add(v.TypedIdent.Type);
            }
            if (f.OutParams.Length != 1)
                throw new Exception("Cannot handle functions with " + f.OutParams + " out parameters.");
            Type range = f.OutParams[0].TypedIdent.Type;

            cm.DeclareFunction(f.Name, domain, range);
        }

        public override void DeclareGlobalVariable(GlobalVariable v, string attributes)
        {
            base.DeclareGlobalVariable(v, attributes);
            cm.DeclareConstant(v.Name, v.TypedIdent.Type);
        }
    }
}

namespace Microsoft.Boogie.Z3api
{
    public class Factory : Microsoft.Boogie.Z3.Factory
    {
        protected override Z3ProcessTheoremProver SpawnProver(VCExpressionGenerator gen,
                                                              DeclFreeProverContext ctx,
                                                              Z3InstanceOptions opts)
        {
            return new Z3apiProcessTheoremProver(gen, ctx, opts);
        }

        protected override DeclFreeProverContext NewProverContext(VCExpressionGenerator gen,
                                                                  VCGenerationOptions genOptions,
                                                                  Z3InstanceOptions opts)
        {
            return new Z3apiProverContext(gen, genOptions, opts);
        }
    }
}