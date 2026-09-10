using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Rulewright.Core;
using Rulewright.Serialization;

namespace Rulewright.Execution;

/// <summary>
/// The Rulewright evaluation engine: loads rule sets (parse → validate → prepare),
/// compiles rules to delegates per fact type via expression trees, caches the compiled
/// delegates by rule content hash, and evaluates facts against them. Thread-safe: one
/// engine instance can serve concurrent evaluations.
///
/// <para>Build one with <see cref="RulewrightBuilder"/>, which also carries the
/// <c>MatchesRegex</c> match timeout — see <see cref="RulewrightBuilder.UseRegexTimeout"/>.</para>
/// </summary>
public sealed class RulewrightEngine
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyOutputs =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    private readonly IRuleJsonReader? _jsonReader;
    private readonly IReadOnlyDictionary<string, IRuleFunction> _functions;
    private readonly TimeSpan _regexTimeout;
    private readonly ReadOnlyCollection<string> _registeredFunctions;
    private readonly ReadOnlyCollection<RuleFunctionDescriptor> _functionCatalog;
    private readonly ConcurrentDictionary<CompiledCacheKey, object> _compiledRules =
        new ConcurrentDictionary<CompiledCacheKey, object>();

    internal RulewrightEngine(
        IRuleJsonReader? jsonReader,
        IReadOnlyDictionary<string, IRuleFunction> functions,
        TimeSpan regexTimeout)
    {
        _jsonReader = jsonReader;
        _functions = functions;
        _regexTimeout = regexTimeout;

        var names = new List<string>(functions.Keys);
        names.Sort(StringComparer.Ordinal);
        _registeredFunctions = new ReadOnlyCollection<string>(names);

        var descriptors = new List<RuleFunctionDescriptor>(names.Count);
        foreach (var name in names)
        {
            var function = functions[name];
            descriptors.Add(function is IRuleFunctionMetadata metadata
                ? new RuleFunctionDescriptor(name, metadata.Description, metadata.ValueKind)
                : new RuleFunctionDescriptor(name, description: null, RuleFunctionValueKind.Unspecified));
        }

        _functionCatalog = new ReadOnlyCollection<RuleFunctionDescriptor>(descriptors);
    }

    /// <summary>
    /// The names of the <c>custom</c> functions registered on this engine, sorted ordinally.
    /// Together with the built-in vocabulary in <see cref="Serialization.RuleSchemaCatalog"/>,
    /// this is the full set of authoring choices a rule-builder UI can offer against this
    /// engine — the one part of the vocabulary that varies per configuration.
    /// </summary>
    public IReadOnlyList<string> RegisteredFunctions => _registeredFunctions;

    /// <summary>
    /// Discovery metadata for the <c>custom</c> functions registered on this engine, in the same
    /// order as <see cref="RegisteredFunctions"/>. Functions that don't implement
    /// <see cref="IRuleFunctionMetadata"/> appear with a null description and
    /// <see cref="RuleFunctionValueKind.Unspecified"/>.
    /// </summary>
    public IReadOnlyList<RuleFunctionDescriptor> FunctionCatalog => _functionCatalog;

    /// <summary>
    /// Parses, validates, and prepares a JSON rule document (a single rule or a rule set)
    /// for evaluation. Parsing and validation happen exactly once here; per-fact-type
    /// compilation happens lazily on first evaluation and is cached by rule content hash.
    /// </summary>
    /// <param name="json">The rule document JSON.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No JSON reader was configured on the builder.</exception>
    /// <exception cref="RuleParseException">The text is not well-formed JSON.</exception>
    /// <exception cref="RuleValidationException">The document fails schema validation.</exception>
    /// <exception cref="RuleCompilationException">A referenced custom function is not registered, or an action type is unknown.</exception>
    public LoadedRuleSet LoadRuleSet(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        RuleJsonValue document = RequireReader().Read(json);
        return LoadRuleSet(RuleSetParser.Parse(document));
    }

    /// <summary>
    /// Prepares an already-constructed domain <see cref="RuleSet"/> for evaluation,
    /// bypassing JSON entirely.
    /// </summary>
    /// <param name="ruleSet">The rule set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ruleSet"/> is null.</exception>
    /// <exception cref="RuleCompilationException">A referenced custom function is not registered, or an action type is unknown.</exception>
    public LoadedRuleSet LoadRuleSet(RuleSet ruleSet)
    {
        if (ruleSet is null)
        {
            throw new ArgumentNullException(nameof(ruleSet));
        }

        IEnumerable<Rule> ordered = ruleSet.Rules.OrderByDescending(rule => rule.Priority);
        var entries = new List<RuleEntry>(ruleSet.Rules.Count);
        foreach (Rule rule in ordered)
        {
            ValidateLeaves(rule, rule.Condition);

            (IReadOnlyDictionary<string, object?> Outputs, bool HasComplex) thenPlan = BuildOutputPlan(rule, rule.Actions);
            (IReadOnlyDictionary<string, object?> Outputs, bool HasComplex) elsePlan = BuildOutputPlan(rule, rule.ElseActions);

            int[] nodeLayout = ConditionNodeIndexer.BuildLayout(rule.Condition);
            entries.Add(new RuleEntry(
                rule,
                RuleHasher.ComputeHash(rule),
                thenPlan.Outputs,
                thenPlan.HasComplex,
                elsePlan.Outputs,
                elsePlan.HasComplex,
                nodeLayout));
        }

        return new LoadedRuleSet(ruleSet, entries.ToArray());
    }

    /// <summary>
    /// Validates a branch's action types and pre-materializes the outputs of a branch made
    /// purely of constant <c>setOutput</c> actions (so it can reuse one shared dictionary
    /// across evaluations). A branch with any computed, accumulating, or <c>removeOutput</c>
    /// action is marked complex and applied per evaluation instead.
    /// </summary>
    private static (IReadOnlyDictionary<string, object?> Outputs, bool HasComplex) BuildOutputPlan(
        Rule rule, IReadOnlyList<RuleAction> actions)
    {
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        bool hasComplex = false;
        foreach (RuleAction action in actions)
        {
            if (!string.Equals(action.Type, RuleAction.SetOutputType, StringComparison.Ordinal)
                && !string.Equals(action.Type, RuleAction.AddToOutputType, StringComparison.Ordinal)
                && !string.Equals(action.Type, RuleAction.AppendToOutputType, StringComparison.Ordinal)
                && !string.Equals(action.Type, RuleAction.RemoveOutputType, StringComparison.Ordinal))
            {
                throw new RuleCompilationException(rule.Id, $"unknown action type '{action.Type}'.");
            }

            if (OutputApplier.IsLiteralSet(action))
            {
                outputs[action.Target] = ((LiteralExpression)action.Value).Value;
            }
            else
            {
                // Computed, accumulating, or removeOutput actions apply to the running result
                // per evaluation rather than being pre-materialized here.
                hasComplex = true;
            }
        }

        return (hasComplex ? EmptyOutputs : new ReadOnlyDictionary<string, object?>(outputs), hasComplex);
    }

    /// <summary>
    /// Validates JSON against the Rulewright schema contract without loading it,
    /// returning structured errors with JSON pointer paths. Malformed JSON is reported
    /// as a single root-level error rather than an exception, making this directly
    /// bindable from editor/UI validation.
    /// </summary>
    /// <param name="json">The rule document JSON.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="InvalidOperationException">No JSON reader was configured on the builder.</exception>
    public RuleSetValidationResult Validate(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        RuleJsonValue document;
        try
        {
            document = RequireReader().Read(json);
        }
        catch (RuleParseException ex)
        {
            return new RuleSetValidationResult(new[] { new RuleValidationError(string.Empty, ex.Message) });
        }

        return RuleSetValidator.Validate(document);
    }

    /// <summary>
    /// Evaluates a fact against a loaded rule set. Typed facts run compiled delegates
    /// (cached per fact type and rule content hash); dictionary facts
    /// (<c>IDictionary&lt;string, object&gt;</c>) run the interpreter, reported via
    /// <see cref="RuleEvaluationResult.CompilationMode"/>.
    /// </summary>
    /// <typeparam name="TFact">The fact type.</typeparam>
    /// <param name="ruleSet">A rule set from <see cref="LoadRuleSet(string)"/>.</param>
    /// <param name="fact">The fact instance.</param>
    /// <param name="options">Per-evaluation options; defaults to <see cref="EvaluationOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ruleSet"/> or <paramref name="fact"/> is null.</exception>
    /// <exception cref="RuleCompilationException">A rule cannot be compiled against <typeparamref name="TFact"/> (missing field path, incompatible value type).</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A <c>MatchesRegex</c> match exceeded the engine's regex timeout.</exception>
    public RuleEvaluationResult Evaluate<TFact>(LoadedRuleSet ruleSet, TFact fact, EvaluationOptions? options = null)
    {
        if (ruleSet is null)
        {
            throw new ArgumentNullException(nameof(ruleSet));
        }

        if (fact is null)
        {
            throw new ArgumentNullException(nameof(fact));
        }

        options ??= EvaluationOptions.Default;

        // Dictionary facts have no compile-time shape. Neither does a fact whose *static* type is
        // object: field paths would be compiled against System.Object and bind to nothing, so the
        // interpreter (which resolves against the runtime type) is the honest path, and
        // CompilationMode.Interpreted reports the degradation rather than hiding it.
        if (fact is System.Collections.IDictionary or IDictionary<string, object?> or IReadOnlyDictionary<string, object?>
            || typeof(TFact) == typeof(object))
        {
            object boxedFact = fact;
            return EvaluateCore(
                ruleSet,
                options,
                CompilationMode.Interpreted,
                (entry, results) => RuleInterpreter.Evaluate(
                    entry.Rule.Condition, 0, boxedFact, _functions, _regexTimeout, results, entry.NodeLayout),
                (entry, isElse, running) => ApplyInterpretedOutputs(entry, isElse, boxedFact, running));
        }

        return EvaluateCore(
            ruleSet,
            options,
            CompilationMode.Compiled,
            (entry, results) =>
            {
                CompiledRule<TFact> compiled = GetOrCompile<TFact>(entry);
                return results is null ? compiled.Predicate(fact) : compiled.TracedPredicate(fact, results);
            },
            (entry, isElse, running) => ApplyCompiledOutputs<TFact>(entry, isElse, fact, running));
    }

    /// <summary>
    /// Applies a fired rule branch's outputs to the running result and returns the rule's own
    /// view of what it wrote. A branch made purely of constant <c>setOutput</c> actions
    /// overwrites its shared, pre-materialized outputs; otherwise each action's value is
    /// evaluated and applied (set/add/append/remove) in order via <see cref="OutputApplier"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ApplyInterpretedOutputs(
        RuleEntry entry, bool isElse, object fact, IDictionary<string, object?> running)
    {
        IReadOnlyList<RuleAction> actions = isElse ? entry.Rule.ElseActions : entry.Rule.Actions;
        bool hasComplex = isElse ? entry.HasComplexElseOutputs : entry.HasComplexOutputs;
        IReadOnlyDictionary<string, object?> prematerialized = isElse ? entry.ElseOutputs : entry.Outputs;

        if (!hasComplex)
        {
            foreach (KeyValuePair<string, object?> output in prematerialized)
            {
                running[output.Key] = output.Value;
            }

            return prematerialized;
        }

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (RuleAction action in actions)
        {
            object? value = ActionExpressionInterpreter.EvaluateValue(action.Value, fact);
            OutputApplier.Apply(running, snapshot, action.Type, action.Target, value);
        }

        return new ReadOnlyDictionary<string, object?>(snapshot);
    }

    private IReadOnlyDictionary<string, object?> ApplyCompiledOutputs<TFact>(
        RuleEntry entry, bool isElse, TFact fact, IDictionary<string, object?> running)
    {
        bool hasComplex = isElse ? entry.HasComplexElseOutputs : entry.HasComplexOutputs;
        IReadOnlyDictionary<string, object?> prematerialized = isElse ? entry.ElseOutputs : entry.Outputs;

        if (!hasComplex)
        {
            foreach (KeyValuePair<string, object?> output in prematerialized)
            {
                running[output.Key] = output.Value;
            }

            return prematerialized;
        }

        CompiledRule<TFact> compiled = GetOrCompile<TFact>(entry);
        OutputStep<TFact>[] steps = (isElse ? compiled.ElseOutputSteps : compiled.OutputSteps)!;
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (OutputStep<TFact> step in steps)
        {
            object? value = step.ValueFactory(fact);
            OutputApplier.Apply(running, snapshot, step.Type, step.Target, value);
        }

        return new ReadOnlyDictionary<string, object?>(snapshot);
    }

    private RuleEvaluationResult EvaluateCore(
        LoadedRuleSet loaded,
        EvaluationOptions options,
        CompilationMode mode,
        Func<RuleEntry, bool?[]?, bool> evaluateRule,
        Func<RuleEntry, bool, IDictionary<string, object?>, IReadOnlyDictionary<string, object?>> applyOutputs)
    {
        List<RuleTrace>? traceRules = options.EnableTrace
            ? new List<RuleTrace>(loaded.OrderedRules.Length)
            : null;
        var fired = new List<FiredRule>();
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        bool stopped = false;

        // The set's own policy (a `first` decision table) and the caller's option combine with OR:
        // a caller may stop a collecting set early, but cannot turn a `first` table into one.
        bool stopOnFirstMatch = options.StopOnFirstMatch || loaded.RuleSet.StopAfterFirstMatch;

        foreach (RuleEntry entry in loaded.OrderedRules)
        {
            if (stopped || !entry.Rule.Enabled)
            {
                // Disabled wins when both apply: it is a property of the rule itself, true wherever
                // the rule sits, whereas stopping is an accident of where the match landed.
                traceRules?.Add(new RuleTrace(
                    entry.Rule.Id,
                    fired: false,
                    entry.Rule.Enabled ? RuleSkipReason.StoppedAfterMatch : RuleSkipReason.Disabled,
                    condition: null));
                continue;
            }

            bool matched;
            ConditionTraceNode? conditionTrace = null;
            if (traceRules is not null)
            {
                var results = new bool?[entry.NodeLayout.Length];
                matched = evaluateRule(entry, results);
                conditionTrace = ConditionTraceBuilder.Build(entry.Rule.Condition, 0, entry.NodeLayout, results);
            }
            else
            {
                matched = evaluateRule(entry, null);
            }

            traceRules?.Add(new RuleTrace(entry.Rule.Id, matched, RuleSkipReason.None, conditionTrace));

            if (matched)
            {
                // applyOutputs writes into the running outputs (overwrite, add, append, or
                // remove) and returns this rule's own view of what it wrote.
                IReadOnlyDictionary<string, object?> ruleOutputs = applyOutputs(entry, false, outputs);
                fired.Add(new FiredRule(entry.Rule.Id, ruleOutputs, RuleBranch.Then));

                if (stopOnFirstMatch)
                {
                    stopped = true;
                }
            }
            else if (entry.HasElse)
            {
                // The condition failed but the rule has an else branch: apply it. This is not a
                // match, so it never triggers stop-on-first-match.
                IReadOnlyDictionary<string, object?> ruleOutputs = applyOutputs(entry, true, outputs);
                fired.Add(new FiredRule(entry.Rule.Id, ruleOutputs, RuleBranch.Else));
            }
        }

        return new RuleEvaluationResult(
            fired,
            new ReadOnlyDictionary<string, object?>(outputs),
            mode,
            traceRules is null ? null : new EvaluationTrace(traceRules));
    }

    private CompiledRule<TFact> GetOrCompile<TFact>(RuleEntry entry)
    {
        var key = new CompiledCacheKey(typeof(TFact), entry.Hash);
        if (_compiledRules.TryGetValue(key, out object? cached))
        {
            return (CompiledRule<TFact>)cached;
        }

        return (CompiledRule<TFact>)_compiledRules.GetOrAdd(
            key,
            _ => RuleExpressionCompiler.Compile<TFact>(entry.Rule, _functions, _regexTimeout, entry.NodeLayout));
    }

    /// <summary>
    /// Checks every leaf's operand against its operator once, at load time, so a malformed rule
    /// fails the same way whichever execution path would have run it. JSON documents are already
    /// screened by <see cref="RuleSetValidator"/>; a hand-built <see cref="RuleSet"/> reaches the
    /// engine unscreened, and without this it would surface as an <see cref="InvalidCastException"/>
    /// from deep inside the compiler (typed facts) or mid-evaluation (dictionary facts).
    /// </summary>
    private void ValidateLeaves(Rule rule, ConditionNode node)
    {
        if (node is ConditionGroup group)
        {
            foreach (ConditionNode child in group.Children)
            {
                ValidateLeaves(rule, child);
            }

            return;
        }

        var leaf = (ConditionLeaf)node;
        switch (leaf.Operator)
        {
            case ConditionOperator.Custom:
                if (!_functions.ContainsKey(leaf.FunctionName!))
                {
                    throw new RuleCompilationException(
                        rule.Id,
                        $"custom function '{leaf.FunctionName}' is not registered. "
                        + "Register it with RulewrightBuilder.RegisterFunction before loading the rule set.");
                }

                break;

            case ConditionOperator.Contains:
            case ConditionOperator.StartsWith:
            case ConditionOperator.EndsWith:
                RequireOperand<string>(rule, leaf, "a string");
                break;

            case ConditionOperator.MatchesRegex:
                RequireOperand<string>(rule, leaf, "a string");
                RequireValidRegex(rule, leaf);
                break;

            case ConditionOperator.In:
            case ConditionOperator.NotIn:
                RequireOperand<object?[]>(rule, leaf, "an array");
                break;

            case ConditionOperator.GreaterThan:
            case ConditionOperator.GreaterThanOrEqual:
            case ConditionOperator.LessThan:
            case ConditionOperator.LessThanOrEqual:
                if (leaf.Value is null)
                {
                    throw new RuleCompilationException(
                        rule.Id,
                        $"operator '{ConditionDescriber.Describe(leaf)}' requires a non-null comparison value.");
                }

                break;
        }
    }

    private static void RequireOperand<TOperand>(Rule rule, ConditionLeaf leaf, string expected)
    {
        if (leaf.Value is TOperand)
        {
            return;
        }

        string actual = leaf.Value is null ? "null" : leaf.Value.GetType().Name;
        throw new RuleCompilationException(
            rule.Id,
            $"operator '{ConditionDescriber.Describe(leaf)}' requires {expected} comparison value, but got {actual}.");
    }

    private void RequireValidRegex(Rule rule, ConditionLeaf leaf)
    {
        try
        {
            _ = new System.Text.RegularExpressions.Regex((string)leaf.Value!, System.Text.RegularExpressions.RegexOptions.None, _regexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new RuleCompilationException(
                rule.Id,
                $"invalid regular expression \"{leaf.Value}\": {ex.Message}",
                ex);
        }
    }

    private IRuleJsonReader RequireReader()
        => _jsonReader ?? throw new InvalidOperationException(
            "No JSON reader is configured. Call RulewrightBuilder.UseJsonReader(...) with an adapter "
            + "such as SystemTextJsonReader (Rulewright.Json.SystemText) before loading JSON.");

    private readonly struct CompiledCacheKey : IEquatable<CompiledCacheKey>
    {
        private readonly Type _factType;
        private readonly string _ruleHash;

        internal CompiledCacheKey(Type factType, string ruleHash)
        {
            _factType = factType;
            _ruleHash = ruleHash;
        }

        public bool Equals(CompiledCacheKey other)
            => ReferenceEquals(_factType, other._factType)
            && string.Equals(_ruleHash, other._ruleHash, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CompiledCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (_factType.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(_ruleHash);
            }
        }
    }
}
