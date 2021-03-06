﻿using System;
using System.Linq.Expressions;
using System.Linq;
using System.Reflection;
using DotVVM.Framework.Utils;
using System.Collections.Generic;
using System.Diagnostics;

namespace DotVVM.Framework.Compilation.Binding
{
    public class ExpressionNullPropagationVisitor : ExpressionVisitor
    {
        public Func<Expression, bool> CanBeNull { get; }

        public ExpressionNullPropagationVisitor(Func<Expression, bool> canBeNull)
        {
            CanBeNull = canBeNull;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression?.Type?.IsNullable() == true)
            {
                if (node.Member.Name == "Value") return node.Expression;
                else return base.VisitMember(node);
            }
            else return CheckForNull(Visit(node.Expression), expr =>
                Expression.MakeMemberAccess(expr, node.Member));

            //var left = Visit(node.Expression);
            //var nullableType = Nullable.GetUnderlyingType(left.Type);
            //if ((!left.Type.GetTypeInfo().IsValueType || nullableType != null) && (CanBeNull == null || CanBeNull(left)))
            //{
            //    var wrapNullable = node.Type.GetTypeInfo().IsValueType;
            //    var restype = wrapNullable ? typeof(Nullable<>).MakeGenericType(node.Type) : node.Type;
            //    var variable = Expression.Parameter(left.Type);
            //    Expression body;
            //    Expression condition;
            //    if (nullableType != null)
            //    {
            //        condition = Expression.Property(variable, "HasValue");
            //        body = node.Update(Expression.Convert(variable, node.Expression.Type));
            //    }
            //    else
            //    {
            //        condition = Expression.NotEqual(variable, Expression.Constant(null, variable.Type));
            //        body = node.Update(variable);
            //    }
            //    if (wrapNullable)
            //    {
            //        body = Expression.Convert(body, restype);
            //    }
            //    return Expression.Block(new[] { variable },
            //        Expression.Assign(variable, left),
            //        Expression.IfThenElse(condition, body, Expression.Default(restype))
            //    );
            //}
            //else return base.VisitMember(node);
        }

        protected override Expression VisitLambda<T>(Expression<T> expression)
        {
            // assert non-null for lambda expressions returning value types
            var body = Visit(expression.Body);
            if (body.Type != expression.ReturnType)
            {
                Debug.Assert(Nullable.GetUnderlyingType(body.Type) == expression.ReturnType);
                body = Expression.Property(body, "Value");
            }
            return expression.Update(body, expression.Parameters);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {

            Expression createExpr(Expression left)
            {
                return CheckForNull(Visit(node.Right), right =>
                                Expression.MakeBinary(node.NodeType, left, right, false, node.Method, node.Conversion),
                            checkReferenceTypes: false);
            }

            if (node.NodeType.ToString().EndsWith("Assign"))
            {
                // only check for left target's null, assignment to null is perfectly valid
                if (node.Left is MemberExpression memberExpression)
                {
                    return CheckForNull(Visit(memberExpression.Expression), memberTarget =>
                        createExpr(memberExpression.Update(memberTarget)));
                }
                else if (node.Left is IndexExpression indexer)
                {
                    return CheckForNull(Visit(indexer.Object), memberTarget =>
                        createExpr(indexer.Update(memberTarget, indexer.Arguments)));
                }
                // this should only be ParameterExpression
                else return createExpr(node.Left);
            }
            else
            {
                if (true)
                {
                    var left = Visit(node.Left);
                    var right = Visit(node.Right);
                    var nullable = left.Type.IsNullable() ? left.Type : right.Type;
                    left = TypeConversion.ImplicitConversion(left, nullable);
                    right = TypeConversion.ImplicitConversion(right, nullable);

                    if (right != null && left != null)
                        return Expression.MakeBinary(node.NodeType, left, right, left.Type.IsNullable() && node.NodeType != ExpressionType.Equal && node.NodeType != ExpressionType.NotEqual, node.Method);
                    else return CheckForNull(base.Visit(node.Left), left2 =>
                        createExpr(left2),
                    checkReferenceTypes: false);
                }
                else
                {
                    
                }
            }
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            return CheckForNull(Visit(node.Operand), operand =>
                Expression.MakeUnary(node.NodeType, operand, node.Type, node.Method),
                checkReferenceTypes: node.Method == null);
        }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            return CheckForNull(Visit(node.Expression), target =>
                CheckForNulls(node.Arguments.Select(Visit).ToArray(), args =>
                    Expression.Invoke(target, args),
                    suppressThisOne: (arg, i) => node.Arguments[i].Type.IsNullable() || !arg.Type.GetTypeInfo().IsValueType),
                suppress: node.Expression.Type.IsNullable()
            );
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            return CheckForNull(Visit(node.Test), test => {
                var ifTrue = Visit(node.IfTrue);
                var ifFalse = Visit(node.IfFalse);
                if (ifTrue.Type != ifFalse.Type)
                {
                    var nullable = ifTrue.Type.IsNullable() ? ifTrue.Type : ifFalse.Type;
                    ifTrue = TypeConversion.ImplicitConversion(ifTrue, nullable);
                    ifFalse = TypeConversion.ImplicitConversion(ifFalse, nullable);
                }
                return Expression.Condition(test, ifTrue, ifFalse);
            });
        }

        protected override Expression VisitIndex(IndexExpression node)
        {
            return CheckForNull(Visit(node.Object), target =>
                CheckForNulls(node.Arguments.Select(Visit).ToArray(), args =>
                    Expression.MakeIndex(target, node.Indexer, args),
                    suppressThisOne: (arg, i) => node.Arguments[i].Type.IsNullable() || !arg.Type.GetTypeInfo().IsValueType),
                suppress: node.Object.Type.IsNullable()
            );
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            return CheckForNull(Visit(node.Object), target =>
                CheckForNulls(node.Arguments.Select(Visit).ToArray(), args =>
                    Expression.Call(target, node.Method, args),
                    suppressThisOne: (arg, i) => node.Arguments[i].Type.IsNullable() || !arg.Type.GetTypeInfo().IsValueType),
                suppress: node.Object?.Type?.IsNullable() ?? true
            );
        }
        protected Expression CheckForNulls(Expression[] parameters, Func<Expression[], Expression> callback, Func<Expression, int, bool> suppressThisOne = null)
        {
            if (parameters.Length == 0) return callback(new Expression[0]);
            var list = new List<Expression>();
            Func<Expression, Expression> cc = e => { list.Add(e); return callback(list.ToArray()); };
            for (var i = parameters.Length - 1; i >= 1; i--)
            {
                var iCopy = i;
                var ccc = cc;
                cc = e => { list.Add(e); return CheckForNull(parameters[iCopy], ccc, suppress: suppressThisOne?.Invoke(parameters[iCopy], iCopy) ?? false); };
            }
            return CheckForNull(parameters[0], cc, suppress: suppressThisOne?.Invoke(parameters[0], 0) ?? false);
        }

        private int tmpCounter;
        protected Expression CheckForNull(Expression parameter, Func<Expression, Expression> callback, bool checkReferenceTypes = true, bool suppress = false)
        {
            if (suppress || parameter == null || (parameter.Type.GetTypeInfo().IsValueType && !parameter.Type.IsNullable()) || !checkReferenceTypes && !parameter.Type.GetTypeInfo().IsValueType)
                return callback(parameter);
            var p2 = Expression.Parameter(parameter.Type, "tmp" + tmpCounter++);
            var eresult = callback(p2.Type.IsNullable() ? (Expression)Expression.Property(p2, "Value") : p2);
            eresult = TypeConversion.ImplicitConversion(eresult, eresult.Type.MakeNullableType());
            return Expression.Block(
                new[] { p2 },
                Expression.Assign(p2, parameter),
                Expression.Condition(parameter.Type.IsNullable() ? (Expression)Expression.Property(p2, "HasValue") : Expression.NotEqual(p2, Expression.Constant(null, p2.Type)),
                    eresult,
                    Expression.Default(eresult.Type)));
        }

        public static Expression PropagateNulls(Expression expr, Func<Expression, bool> canBeNull)
        {
            var v = new ExpressionNullPropagationVisitor(canBeNull);
            return v.Visit(expr);
        }
    }
}
