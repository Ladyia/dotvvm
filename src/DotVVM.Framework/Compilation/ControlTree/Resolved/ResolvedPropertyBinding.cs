﻿using System.Diagnostics;
using DotVVM.Framework.Binding;

namespace DotVVM.Framework.Compilation.ControlTree.Resolved
{
    [DebuggerDisplay("{Property}='{{Binding}}'")]
    public class ResolvedPropertyBinding : ResolvedPropertySetter, IAbstractPropertyBinding
    {
        public ResolvedBinding Binding { get; set; }

        public ResolvedPropertyBinding(DotvvmProperty property, ResolvedBinding binding) : base(property)
        {
            Binding = binding;
            binding.Parent = this;
        }

        public override void Accept(IResolvedControlTreeVisitor visitor)
        {
            visitor.VisitPropertyBinding(this);
        }

        public override void AcceptChildren(IResolvedControlTreeVisitor visitor)
        {
            Binding?.Accept(visitor);
        }

        IAbstractBinding IAbstractPropertyBinding.Binding => Binding;
    }
}
