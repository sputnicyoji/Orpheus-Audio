using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioHotPathContractTests
    {
        [Test]
        public void RuntimeHotPathCallGraph_ContainsNoProhibitedOperations()
        {
            var roots = new[]
            {
                RequireMethod(typeof(OrpheusAudioManager), "Play", typeof(OrpheusAudioKey)),
                RequireMethod(
                    typeof(OrpheusAudioManager),
                    "PlayAt",
                    typeof(OrpheusAudioKey),
                    typeof(Vector3)),
                RequireMethod(typeof(OrpheusAudioManager), "PlayLoop", typeof(OrpheusAudioKey)),
                RequireMethod(typeof(OrpheusAudioManager), "StopLoop", typeof(OrpheusAudioKey)),
                RequireMethod(typeof(OrpheusAudioBridge), "Bind", typeof(OrpheusAudioManager)),
                RequireMethod(typeof(OrpheusAudioBridge), "Unbind", typeof(OrpheusAudioManager)),
                RequireMethod(typeof(OrpheusAudioBridge), "Play", typeof(OrpheusAudioKey)),
                RequireMethod(
                    typeof(OrpheusAudioBridge),
                    "PlayAt",
                    typeof(OrpheusAudioKey),
                    typeof(Vector3)),
                RequireMethod(typeof(OrpheusAudioRawBridge), "Bind", typeof(OrpheusAudioManager)),
                RequireMethod(typeof(OrpheusAudioRawBridge), "Unbind", typeof(OrpheusAudioManager)),
                RequireMethod(typeof(OrpheusAudioRawBridge), "Play", typeof(int)),
                RequireMethod(
                    typeof(OrpheusAudioRawBridge),
                    "PlayAt",
                    typeof(int),
                    typeof(Vector3)),
                RequireMethod(typeof(OrpheusAudioManager), "Tick", typeof(double), typeof(float)),
                RequireMethod(typeof(OrpheusAudioManager), "LateTick"),
                RequireMethod(typeof(OrpheusAudioRuntimeHost), "Update"),
                RequireMethod(typeof(OrpheusAudioRuntimeHost), "LateUpdate"),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "OnApplicationFocus",
                    typeof(bool)),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "OnApplicationPause",
                    typeof(bool)),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "HandleAudioConfigurationChanged",
                    typeof(bool)),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "HandleApplicationFocusChanged",
                    typeof(bool)),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "HandleApplicationPauseChanged",
                    typeof(bool)),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "RecordAudioConfigurationChanged",
                    typeof(bool)),
                RequireMethod(
                    typeof(OrpheusAudioRuntimeHost),
                    "ConsumePendingAudioConfigurationChanges")
            };

            var violations = HotPathCallGraph.FindViolations(roots);

            Assert.That(
                violations,
                Is.Empty,
                "Hot-path contract violations:\n" + string.Join("\n", violations));
        }

        [Test]
        public void StaticGate_RejectsEveryProhibitedOperationClass()
        {
            var roots = new[]
            {
                RequireMethod(typeof(SyntheticHotPaths), "DynamicCollection"),
                RequireMethod(typeof(SyntheticHotPaths), "Linq"),
                RequireMethod(typeof(SyntheticHotPaths), "Iterator"),
                RequireMethod(typeof(SyntheticHotPaths), "Closure"),
                RequireMethod(typeof(SyntheticHotPaths), "Boxing"),
                RequireMethod(typeof(SyntheticHotPaths), "Reflection"),
                RequireMethod(typeof(SyntheticHotPaths), "ParamsArray"),
                RequireMethod(typeof(SyntheticHotPaths), "InterpolatedString"),
                RequireMethod(typeof(SyntheticHotPaths), "StringConcatenation"),
                RequireMethod(typeof(SyntheticHotPaths), "DebugLog"),
                RequireMethod(typeof(SyntheticHotPaths), "ReferenceAllocation")
            };

            var violations = HotPathCallGraph.FindViolations(roots);
            var codes = new HashSet<string>(violations.Select(GetViolationCode));

            Assert.That(codes, Does.Contain("DynamicCollection"));
            Assert.That(codes, Does.Contain("Linq"));
            Assert.That(codes, Does.Contain("Iterator"));
            Assert.That(codes, Does.Contain("Closure"));
            Assert.That(codes, Does.Contain("Boxing"));
            Assert.That(codes, Does.Contain("Reflection"));
            Assert.That(codes, Does.Contain("ParamsArray"));
            Assert.That(codes, Does.Contain("InterpolatedString"));
            Assert.That(codes, Does.Contain("StringConcatenation"));
            Assert.That(codes, Does.Contain("DebugLog"));
            Assert.That(codes, Does.Contain("NonColdNew"));
        }

        [Test]
        public void StaticGate_DoesNotScanUnreachableColdPaths()
        {
            var root = RequireMethod(typeof(SyntheticHotPaths), "SafeRoot");

            var violations = HotPathCallGraph.FindViolations(new[] { root });

            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void StaticGate_RejectsUnconditionalExceptionConstruction()
        {
            var root = RequireMethod(typeof(SyntheticHotPaths), "ExceptionalGuard");

            var violations = HotPathCallGraph.FindViolations(new[] { root });

            Assert.That(violations.Select(GetViolationCode), Does.Contain("NonColdNew"));
        }

        [Test]
        public void StaticGate_AcceptsConditionallyControlledExceptionFailurePath()
        {
            var root = RequireMethod(
                typeof(SyntheticHotPaths),
                "ConditionalExceptionalGuard",
                typeof(bool));

            var violations = HotPathCallGraph.FindViolations(new[] { root });

            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void StaticGate_TraversesInterfaceDispatchToConcreteBodies()
        {
            var root = RequireMethod(typeof(SyntheticHotPaths), "InterfaceDispatch");

            var violations = HotPathCallGraph.FindViolations(new[] { root });

            Assert.That(violations.Select(GetViolationCode), Does.Contain("DebugLog"));
        }

        [TestCase("Queue")]
        [TestCase("Stack")]
        [TestCase("LinkedList")]
        [TestCase("SortedList")]
        [TestCase("SortedDictionary")]
        [TestCase("ConcurrentQueue")]
        [TestCase("ConcurrentStack")]
        [TestCase("ConcurrentBag")]
        [TestCase("ConcurrentDictionary")]
        public void StaticGate_RejectsMutableDynamicCollectionFamily(string methodName)
        {
            var root = RequireMethod(typeof(SyntheticHotPaths), methodName);

            var violations = HotPathCallGraph.FindViolations(new[] { root });

            Assert.That(violations.Select(GetViolationCode), Does.Contain("DynamicCollection"));
        }

        [Test]
        public void StaticGate_DoesNotClassifyReadOnlyCollectionsOrValueEnumeratorsAsMutable()
        {
            var roots = new[]
            {
                RequireMethod(
                    typeof(SyntheticHotPaths),
                    "ReadOnlyCollectionAccess",
                    typeof(ReadOnlyCollection<int>)),
                RequireMethod(
                    typeof(SyntheticHotPaths),
                    "ValueEnumeratorAccess",
                    typeof(List<int>.Enumerator))
            };

            var violations = HotPathCallGraph.FindViolations(roots);

            Assert.That(violations.Select(GetViolationCode), Does.Not.Contain("DynamicCollection"));
        }

        private static string GetViolationCode(string violation)
        {
            var separator = violation.IndexOf(':');
            return separator < 0 ? violation : violation.Substring(0, separator);
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameterTypes)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static,
                null,
                parameterTypes,
                null);
            Assert.That(method, Is.Not.Null, type.FullName + "." + name);
            return method;
        }

        private static class HotPathCallGraph
        {
            private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
            private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

            static HotPathCallGraph()
            {
                foreach (var field in typeof(OpCodes).GetFields(
                    BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.FieldType != typeof(OpCode))
                    {
                        continue;
                    }

                    var opCode = (OpCode)field.GetValue(null);
                    var value = unchecked((ushort)opCode.Value);
                    if (value < 0x100)
                    {
                        OneByteOpCodes[value] = opCode;
                    }
                    else if ((value & 0xff00) == 0xfe00)
                    {
                        TwoByteOpCodes[value & 0xff] = opCode;
                    }
                }
            }

            internal static string[] FindViolations(IEnumerable<MethodInfo> roots)
            {
                var pending = new Stack<MethodBase>();
                var visited = new HashSet<MethodBase>();
                var allowedAssemblies = new HashSet<Assembly>();
                var violations = new HashSet<string>(StringComparer.Ordinal);

                foreach (var root in roots)
                {
                    pending.Push(root);
                    allowedAssemblies.Add(root.DeclaringType.Assembly);
                }

                allowedAssemblies.Add(typeof(OrpheusAudioManager).Assembly);
                allowedAssemblies.Add(typeof(OrpheusAudioKey).Assembly);

                while (pending.Count > 0)
                {
                    var method = pending.Pop();
                    if (!visited.Add(method))
                    {
                        continue;
                    }

                    InspectMethod(method, allowedAssemblies, pending, violations);
                }

                return violations.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            }

            private static void InspectMethod(
                MethodBase method,
                HashSet<Assembly> allowedAssemblies,
                Stack<MethodBase> pending,
                HashSet<string> violations)
            {
                if (method.IsDefined(typeof(System.Runtime.CompilerServices.IteratorStateMachineAttribute), false))
                {
                    AddViolation(violations, "Iterator", method, null);
                }

                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.IsDefined(typeof(ParamArrayAttribute), false))
                    {
                        AddViolation(violations, "ParamsArray", method, null);
                    }
                }

                var body = method.GetMethodBody();
                if (body == null)
                {
                    return;
                }

                var instructions = DecodeInstructions(body.GetILAsByteArray());
                for (var index = 0; index < instructions.Count; index++)
                {
                    var instruction = instructions[index];
                    var opCode = instruction.OpCode;

                    if (opCode == OpCodes.Box)
                    {
                        AddViolation(violations, "Boxing", method, null);
                    }

                    if (opCode.OperandType == OperandType.InlineMethod)
                    {
                        var token = BitConverter.ToInt32(
                            instruction.Il,
                            instruction.OperandOffset);
                        var target = ResolveMethod(method, token);
                        if (target != null)
                        {
                            InspectCall(
                                method,
                                target,
                                opCode,
                                IsConditionallyColdExceptionConstruction(
                                    target,
                                    instructions,
                                    index),
                                allowedAssemblies,
                                pending,
                                violations);
                        }
                    }
                    else if (opCode == OpCodes.Newarr)
                    {
                        AddViolation(violations, "NonColdNew", method, null);
                    }
                }
            }

            private static void InspectCall(
                MethodBase caller,
                MethodBase target,
                OpCode opCode,
                bool conditionallyColdExceptionConstruction,
                HashSet<Assembly> allowedAssemblies,
                Stack<MethodBase> pending,
                HashSet<string> violations)
            {
                var declaringType = target.DeclaringType;
                var genericType = declaringType != null && declaringType.IsGenericType
                    ? declaringType.GetGenericTypeDefinition()
                    : declaringType;

                if (IsMutableDynamicCollectionType(genericType))
                {
                    AddViolation(violations, "DynamicCollection", caller, target);
                }

                if (declaringType != null && declaringType.Namespace == "System.Linq")
                {
                    AddViolation(violations, "Linq", caller, target);
                }

                if (declaringType != null &&
                    (declaringType.Namespace == "System.Reflection" ||
                     declaringType == typeof(Activator) ||
                     (declaringType == typeof(Type) && target.Name != "GetTypeFromHandle") ||
                     (declaringType == typeof(object) && target.Name == "GetType")))
                {
                    AddViolation(violations, "Reflection", caller, target);
                }

                if (declaringType == typeof(string) && target.Name == "Concat")
                {
                    AddViolation(violations, "StringConcatenation", caller, target);
                }

                if (declaringType == typeof(string) && target.Name == "Format")
                {
                    AddViolation(violations, "InterpolatedString", caller, target);
                }

                if (declaringType != null &&
                    declaringType.FullName == "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")
                {
                    AddViolation(violations, "InterpolatedString", caller, target);
                }

                if (declaringType == typeof(Debug) && target.Name.StartsWith("Log", StringComparison.Ordinal))
                {
                    AddViolation(violations, "DebugLog", caller, target);
                }

                foreach (var parameter in target.GetParameters())
                {
                    if (parameter.IsDefined(typeof(ParamArrayAttribute), false))
                    {
                        AddViolation(violations, "ParamsArray", caller, target);
                    }
                }

                if (opCode == OpCodes.Newobj &&
                    declaringType != null &&
                    !declaringType.IsValueType &&
                    (!typeof(Exception).IsAssignableFrom(declaringType) ||
                     !conditionallyColdExceptionConstruction))
                {
                    AddViolation(violations, "NonColdNew", caller, target);
                    if (typeof(Delegate).IsAssignableFrom(declaringType) ||
                        IsCompilerGenerated(declaringType))
                    {
                        AddViolation(violations, "Closure", caller, target);
                    }
                }

                if (declaringType != null &&
                    allowedAssemblies.Contains(declaringType.Assembly) &&
                    target.GetMethodBody() != null)
                {
                    pending.Push(target);
                }

                if (declaringType != null && declaringType.IsInterface)
                {
                    EnqueueInterfaceImplementations(target, allowedAssemblies, pending);
                }
            }

            private static bool IsMutableDynamicCollectionType(Type type)
            {
                if (type == null || type.IsValueType || type.Namespace == null ||
                    !type.Namespace.StartsWith("System.Collections", StringComparison.Ordinal) ||
                    type.Name.StartsWith("ReadOnly", StringComparison.Ordinal))
                {
                    return false;
                }

                if (type.Namespace.StartsWith(
                        "System.Collections.Immutable",
                        StringComparison.Ordinal) &&
                    type.Name.IndexOf("Builder", StringComparison.Ordinal) < 0)
                {
                    return false;
                }

                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    switch (method.Name)
                    {
                        case "Add":
                        case "AddAfter":
                        case "AddBefore":
                        case "AddFirst":
                        case "AddLast":
                        case "AddOrUpdate":
                        case "Clear":
                        case "CompleteAdding":
                        case "Dequeue":
                        case "Enqueue":
                        case "ExceptWith":
                        case "GetOrAdd":
                        case "Insert":
                        case "IntersectWith":
                        case "Pop":
                        case "Push":
                        case "Remove":
                        case "RemoveAt":
                        case "RemoveFirst":
                        case "RemoveLast":
                        case "SymmetricExceptWith":
                        case "TryAdd":
                        case "TryDequeue":
                        case "TryPop":
                        case "TryRemove":
                        case "TryTake":
                        case "UnionWith":
                        case "set_Item":
                            return true;
                    }
                }

                return false;
            }

            private static bool IsConditionallyColdExceptionConstruction(
                MethodBase target,
                List<IlInstruction> instructions,
                int constructorIndex)
            {
                var declaringType = target.DeclaringType;
                if (declaringType == null ||
                    !typeof(Exception).IsAssignableFrom(declaringType) ||
                    !instructions.Any(instruction => instruction.OpCode == OpCodes.Ret))
                {
                    return false;
                }

                var throwIndex = constructorIndex + 1;
                while (throwIndex < instructions.Count &&
                    instructions[throwIndex].OpCode == OpCodes.Nop)
                {
                    throwIndex++;
                }

                if (throwIndex >= instructions.Count ||
                    instructions[throwIndex].OpCode != OpCodes.Throw)
                {
                    return false;
                }

                for (var branchIndex = 0; branchIndex < instructions.Count; branchIndex++)
                {
                    var instruction = instructions[branchIndex];
                    if (instruction.OpCode.FlowControl != FlowControl.Cond_Branch)
                    {
                        continue;
                    }

                    if (!CanReachInstruction(instructions, 0, branchIndex, -1))
                    {
                        continue;
                    }

                    var successors = GetSuccessorIndices(instructions, branchIndex);
                    var hasThrowSuccessor = successors.Any(successor =>
                        CanReachInstruction(
                            instructions,
                            successor,
                            constructorIndex,
                            -1));
                    var hasNonThrowReturnSuccessor = successors.Any(successor =>
                        CanReachReturn(
                            instructions,
                            successor,
                            constructorIndex));
                    if (hasThrowSuccessor && hasNonThrowReturnSuccessor)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool CanReachInstruction(
                List<IlInstruction> instructions,
                int startIndex,
                int targetIndex,
                int avoidedIndex)
            {
                if (startIndex < 0 || startIndex >= instructions.Count)
                {
                    return false;
                }

                var pending = new Stack<int>();
                var visited = new HashSet<int>();
                pending.Push(startIndex);
                while (pending.Count > 0)
                {
                    var index = pending.Pop();
                    if (index == avoidedIndex || !visited.Add(index))
                    {
                        continue;
                    }

                    if (index == targetIndex)
                    {
                        return true;
                    }

                    foreach (var successor in GetSuccessorIndices(instructions, index))
                    {
                        pending.Push(successor);
                    }
                }

                return false;
            }

            private static bool CanReachReturn(
                List<IlInstruction> instructions,
                int startIndex,
                int avoidedIndex)
            {
                if (startIndex < 0 || startIndex >= instructions.Count)
                {
                    return false;
                }

                var pending = new Stack<int>();
                var visited = new HashSet<int>();
                pending.Push(startIndex);
                while (pending.Count > 0)
                {
                    var index = pending.Pop();
                    if (index == avoidedIndex || !visited.Add(index))
                    {
                        continue;
                    }

                    if (instructions[index].OpCode == OpCodes.Ret)
                    {
                        return true;
                    }

                    foreach (var successor in GetSuccessorIndices(instructions, index))
                    {
                        pending.Push(successor);
                    }
                }

                return false;
            }

            private static int[] GetSuccessorIndices(
                List<IlInstruction> instructions,
                int instructionIndex)
            {
                var instruction = instructions[instructionIndex];
                var successors = new List<int>();
                foreach (var targetOffset in instruction.BranchTargets)
                {
                    var targetIndex = FindInstructionIndex(instructions, targetOffset);
                    if (targetIndex >= 0)
                    {
                        successors.Add(targetIndex);
                    }
                }

                var flowControl = instruction.OpCode.FlowControl;
                if (flowControl != FlowControl.Branch &&
                    flowControl != FlowControl.Return &&
                    flowControl != FlowControl.Throw &&
                    instructionIndex + 1 < instructions.Count)
                {
                    successors.Add(instructionIndex + 1);
                }

                return successors.Distinct().ToArray();
            }

            private static int FindInstructionIndex(
                List<IlInstruction> instructions,
                int offset)
            {
                for (var index = 0; index < instructions.Count; index++)
                {
                    if (instructions[index].Offset == offset)
                    {
                        return index;
                    }
                }

                return -1;
            }

            private static void EnqueueInterfaceImplementations(
                MethodBase interfaceMethod,
                HashSet<Assembly> allowedAssemblies,
                Stack<MethodBase> pending)
            {
                var interfaceType = interfaceMethod.DeclaringType;
                foreach (var assembly in allowedAssemblies)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface ||
                            !interfaceType.IsAssignableFrom(type))
                        {
                            continue;
                        }

                        var map = type.GetInterfaceMap(interfaceType);
                        for (var index = 0; index < map.InterfaceMethods.Length; index++)
                        {
                            if (map.InterfaceMethods[index] == interfaceMethod)
                            {
                                pending.Push(map.TargetMethods[index]);
                                break;
                            }
                        }
                    }
                }
            }

            private static bool IsCompilerGenerated(MemberInfo member)
            {
                return member.IsDefined(
                    typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute),
                    false);
            }

            private static void AddViolation(
                HashSet<string> violations,
                string code,
                MethodBase caller,
                MethodBase target)
            {
                var value = code + ": " + FormatMethod(caller);
                if (target != null)
                {
                    value += " -> " + FormatMethod(target);
                }

                violations.Add(value);
            }

            private static string FormatMethod(MethodBase method)
            {
                return method.DeclaringType.FullName + "." + method.Name;
            }

            private static MethodBase ResolveMethod(MethodBase caller, int token)
            {
                try
                {
                    var declaringArguments = caller.DeclaringType != null && caller.DeclaringType.IsGenericType
                        ? caller.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;
                    var methodInfo = caller as MethodInfo;
                    var methodArguments = methodInfo != null && methodInfo.IsGenericMethod
                        ? methodInfo.GetGenericArguments()
                        : Type.EmptyTypes;
                    return caller.Module.ResolveMethod(token, declaringArguments, methodArguments);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            private static List<IlInstruction> DecodeInstructions(byte[] il)
            {
                var instructions = new List<IlInstruction>();
                var offset = 0;
                while (offset < il.Length)
                {
                    var instructionOffset = offset;
                    var opCode = ReadOpCode(il, ref offset);
                    var operandOffset = offset;
                    var operandSize = GetOperandSize(opCode.OperandType, il, operandOffset);
                    var endOffset = operandOffset + operandSize;
                    var branchTargets = GetBranchTargets(
                        opCode.OperandType,
                        il,
                        operandOffset,
                        endOffset);
                    instructions.Add(new IlInstruction(
                        il,
                        instructionOffset,
                        operandOffset,
                        endOffset,
                        opCode,
                        branchTargets));
                    offset = endOffset;
                }

                return instructions;
            }

            private static int[] GetBranchTargets(
                OperandType operandType,
                byte[] il,
                int operandOffset,
                int endOffset)
            {
                if (operandType == OperandType.ShortInlineBrTarget)
                {
                    return new[] { endOffset + unchecked((sbyte)il[operandOffset]) };
                }

                if (operandType == OperandType.InlineBrTarget)
                {
                    return new[] { endOffset + BitConverter.ToInt32(il, operandOffset) };
                }

                if (operandType != OperandType.InlineSwitch)
                {
                    return new int[0];
                }

                var count = BitConverter.ToInt32(il, operandOffset);
                var targets = new int[count];
                for (var index = 0; index < count; index++)
                {
                    targets[index] = endOffset + BitConverter.ToInt32(
                        il,
                        operandOffset + 4 + (index * 4));
                }

                return targets;
            }

            private static OpCode ReadOpCode(byte[] il, ref int offset)
            {
                var first = il[offset++];
                if (first != 0xfe)
                {
                    return OneByteOpCodes[first];
                }

                return TwoByteOpCodes[il[offset++]];
            }

            private static int GetOperandSize(OperandType operandType, byte[] il, int offset)
            {
                switch (operandType)
                {
                    case OperandType.InlineNone:
                        return 0;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        return 1;
                    case OperandType.InlineVar:
                        return 2;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineI:
                    case OperandType.InlineMethod:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.ShortInlineR:
                        return 4;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        return 8;
                    case OperandType.InlineSwitch:
                        return 4 + (BitConverter.ToInt32(il, offset) * 4);
                    default:
                        throw new InvalidOperationException("Unknown IL operand type " + operandType + ".");
                }
            }

            private sealed class IlInstruction
            {
                internal IlInstruction(
                    byte[] il,
                    int offset,
                    int operandOffset,
                    int endOffset,
                    OpCode opCode,
                    int[] branchTargets)
                {
                    Il = il;
                    Offset = offset;
                    OperandOffset = operandOffset;
                    EndOffset = endOffset;
                    OpCode = opCode;
                    BranchTargets = branchTargets;
                }

                internal byte[] Il { get; }
                internal int Offset { get; }
                internal int OperandOffset { get; }
                internal int EndOffset { get; }
                internal OpCode OpCode { get; }
                internal int[] BranchTargets { get; }
            }
        }

        private static class SyntheticHotPaths
        {
            private static readonly ISyntheticPort Port = new SyntheticPort();

            internal static void DynamicCollection()
            {
                var values = new List<int>();
                values.Add(1);
            }

            internal static void Queue()
            {
                var values = new Queue<int>();
                values.Enqueue(1);
            }

            internal static void Stack()
            {
                var values = new Stack<int>();
                values.Push(1);
            }

            internal static void LinkedList()
            {
                var values = new LinkedList<int>();
                values.AddLast(1);
            }

            internal static void SortedList()
            {
                var values = new SortedList<int, int>();
                values.Add(1, 1);
            }

            internal static void SortedDictionary()
            {
                var values = new SortedDictionary<int, int>();
                values.Add(1, 1);
            }

            internal static void ConcurrentQueue()
            {
                var values = new ConcurrentQueue<int>();
                values.Enqueue(1);
            }

            internal static void ConcurrentStack()
            {
                var values = new ConcurrentStack<int>();
                values.Push(1);
            }

            internal static void ConcurrentBag()
            {
                var values = new ConcurrentBag<int>();
                values.Add(1);
            }

            internal static void ConcurrentDictionary()
            {
                var values = new ConcurrentDictionary<int, int>();
                values.TryAdd(1, 1);
            }

            internal static int ReadOnlyCollectionAccess(ReadOnlyCollection<int> values)
            {
                return values.Count;
            }

            internal static bool ValueEnumeratorAccess(List<int>.Enumerator enumerator)
            {
                return enumerator.MoveNext();
            }

            internal static int Linq()
            {
                return new[] { 1 }.Where(value => value > 0).Count();
            }

            internal static IEnumerable<int> Iterator()
            {
                yield return 1;
            }

            internal static Func<int> Closure()
            {
                var value = 1;
                return () => value;
            }

            internal static object Boxing()
            {
                return 1;
            }

            internal static MethodInfo Reflection()
            {
                return typeof(string).GetMethod("Trim", Type.EmptyTypes);
            }

            internal static int ParamsArray()
            {
                return ParamsHelper(1, 2);
            }

            internal static string InterpolatedString()
            {
                var value = 1;
                return $"value={value}";
            }

            internal static string StringConcatenation()
            {
                return string.Concat("a", "b");
            }

            internal static void DebugLog()
            {
                Debug.Log("bad");
            }

            internal static object ReferenceAllocation()
            {
                return new object();
            }

            internal static int SafeRoot()
            {
                return SafeHelper(1);
            }

            internal static void ExceptionalGuard()
            {
                throw new InvalidOperationException();
            }

            internal static int ConditionalExceptionalGuard(bool invalid)
            {
                if (invalid)
                {
                    throw new InvalidOperationException();
                }

                return 1;
            }

            internal static void InterfaceDispatch()
            {
                Port.Execute();
            }

            private static int SafeHelper(int value)
            {
                return value + 1;
            }

            private static object UnreachableColdAllocation()
            {
                return new object();
            }

            private static int ParamsHelper(params int[] values)
            {
                return values.Length;
            }
        }

        private interface ISyntheticPort
        {
            void Execute();
        }

        private sealed class SyntheticPort : ISyntheticPort
        {
            public void Execute()
            {
                Debug.Log("bad interface implementation");
            }
        }
    }
}
