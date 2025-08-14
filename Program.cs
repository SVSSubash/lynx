
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using static System.Reflection.Metadata.Ecma335.MethodBodyStreamEncoder;

namespace Lynx
{
    public class ConditionalBlock
    {
        public string Type { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public List<int> BranchTargets { get; set; } = new List<int>();
        public string Description { get; set; }
    }
    public static class ILPrinter
    {
        private static readonly OpCode[] singleByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] multiByteOpCodes = new OpCode[0x100];
        private static bool initialized = false;

        private static void EnsureInitialized()
        {
            if (initialized) return;
            foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (fi.GetValue(null) is OpCode op)
                {
                    if (op.Size == 1)
                        singleByteOpCodes[op.Value & 0xFF] = op;
                    else
                        multiByteOpCodes[op.Value & 0xFF] = op;
                }
            }
            initialized = true;
        }

        private static bool IsBranchOpCode(OpCode opCode)
        {
            return opCode == OpCodes.Brtrue || opCode == OpCodes.Brtrue_S ||
                   opCode == OpCodes.Brfalse || opCode == OpCodes.Brfalse_S ||
                   opCode == OpCodes.Beq || opCode == OpCodes.Beq_S ||
                   opCode == OpCodes.Bne_Un || opCode == OpCodes.Bne_Un_S ||
                   opCode == OpCodes.Blt || opCode == OpCodes.Blt_S ||
                   opCode == OpCodes.Ble || opCode == OpCodes.Ble_S ||
                   opCode == OpCodes.Bgt || opCode == OpCodes.Bgt_S ||
                   opCode == OpCodes.Bge || opCode == OpCodes.Bge_S ||
                   opCode == OpCodes.Br || opCode == OpCodes.Br_S;
        }

        private static bool IsExceptionHandlingOpCode(OpCode opCode)
        {
            return opCode == OpCodes.Throw || opCode == OpCodes.Rethrow ||
                   opCode == OpCodes.Leave || opCode == OpCodes.Leave_S ||
                   opCode == OpCodes.Endfinally || opCode == OpCodes.Endfilter;
        }

        private static readonly Dictionary<int, string> _conditionalBlocks = new Dictionary<int, string>();
        private static readonly byte[] _il;
        private static int GetBranchTarget(OpCode opCode, int offset, int opCodeSize)
        {
            try
            {
                if (opCode.OperandType == OperandType.ShortInlineBrTarget)
                {
                    // 1-byte relative offset
                    sbyte relativeOffset = (sbyte)_il[offset + opCodeSize];
                    return offset + opCodeSize + 1 + relativeOffset;
                }
                else if (opCode.OperandType == OperandType.InlineBrTarget)
                {
                    // 4-byte relative offset
                    int relativeOffset = BitConverter.ToInt32(_il, offset + opCodeSize);
                    return offset + opCodeSize + 4 + relativeOffset;
                }
            }
            catch (Exception)
            {
                // Return current offset if calculation fails
            }
            return offset;
        }

        private static string GetBranchType(OpCode opCode)
        {
            if (opCode == OpCodes.Brtrue || opCode == OpCodes.Brtrue_S) return "if true";
            if (opCode == OpCodes.Brfalse || opCode == OpCodes.Brfalse_S) return "if false";
            if (opCode == OpCodes.Beq || opCode == OpCodes.Beq_S) return "if equal";
            if (opCode == OpCodes.Bne_Un || opCode == OpCodes.Bne_Un_S) return "if not equal";
            if (opCode == OpCodes.Blt || opCode == OpCodes.Blt_S) return "if less than";
            if (opCode == OpCodes.Ble || opCode == OpCodes.Ble_S) return "if less or equal";
            if (opCode == OpCodes.Bgt || opCode == OpCodes.Bgt_S) return "if greater than";
            if (opCode == OpCodes.Bge || opCode == OpCodes.Bge_S) return "if greater or equal";
            if (opCode == OpCodes.Br || opCode == OpCodes.Br_S) return "unconditional branch";
            return "conditional branch";
        }
        private static void DetectConditionalBlocks(OpCode opCode, int offset, int opCodeSize)
        {
            try
            {
                // Branch opcodes for if/else statements and loops
                if (IsBranchOpCode(opCode))
                {
                    int target = GetBranchTarget(opCode, offset, opCodeSize);
                    string branchType = GetBranchType(opCode);

                    // Check if this is a backward branch (potential loop)
                    if (target < offset)
                    {
                        // This is a backward branch - likely a loop
                        var loopBlock = new ConditionalBlock
                        {
                            Type = "Loop",
                            StartOffset = offset,
                            Description = "(loop iteration)"
                        };
                        loopBlock.BranchTargets.Add(target);

                        _conditionalBlocks[offset] = loopBlock.Description;
                        Console.WriteLine($"Detected loop at IL_{offset:X4}: {loopBlock.Description} targeting IL_{target:X4}");
                    }
                    else
                    {
                        // This is a forward branch - if/else statement
                        var block = new ConditionalBlock
                        {
                            Type = "Branch",
                            StartOffset = offset,
                            Description = $"({branchType})"
                        };
                        block.BranchTargets.Add(target);

                        _conditionalBlocks[offset] = block.Description;
                        Console.WriteLine($"Detected conditional block at IL_{offset:X4}: {block.Description} targeting IL_{target:X4}");
                    }
                }

                // Switch statement detection
                if (opCode == OpCodes.Switch)
                {
                    var block = new ConditionalBlock
                    {
                        Type = "Switch",
                        StartOffset = offset,
                        Description = "(switch statement)"
                    };

                    _conditionalBlocks[offset] = block.Description;
                    Console.WriteLine($"Detected switch statement at IL_{offset:X4}: {block.Description}");
                }

                // Try/catch blocks detection
                //if (IsExceptionHandlingOpCode(opCode))
                //{
                //    string exceptionType = GetExceptionHandlingType(opCode);
                //    var block = new ConditionalBlock
                //    {
                //        Type = "Exception",
                //        StartOffset = offset,
                //        Description = $"({exceptionType})"
                //    };

                //    _conditionalBlocks[offset] = block.Description;
                //}

                // Remove the generic loop detection since we now handle it in the branch detection
            }
            catch (Exception)
            {
                // Continue analyzing if individual block detection fails
            }
        }

        private static string AnalyzeExceptionHandlers(System.Reflection.MethodBody _methodBody)
        {
            if (_methodBody?.ExceptionHandlingClauses == null) return string.Empty;
            foreach (var clause in _methodBody.ExceptionHandlingClauses)
            {
                Console.WriteLine($"Exception Handling Clause: {clause.Flags}");
                Console.WriteLine(clause.TryOffset);
                Console.WriteLine($"Try Offset: {clause.TryOffset}, Length: {clause.TryLength}");
                Console.WriteLine($"Handler Offset: {clause.HandlerOffset}, Length: {clause.HandlerLength}");
            }
            int i = 0;
            Dictionary<int, int> tryOffset = new Dictionary<int, int>();
            foreach (var clause in _methodBody.ExceptionHandlingClauses)
            {
                for (int offset = clause.TryOffset; offset < clause.TryOffset + clause.TryLength; offset++)
                {
                    //_exceptionHandlerContext[offset] = "try block";
                    if (tryOffset.ContainsKey(clause.TryOffset))
                    {
                        continue;
                    }
                    else
                    {
                        tryOffset[offset] = i;
                    }
                    Console.WriteLine(i);
                    Console.WriteLine("try");
                    i++;
                }
            }
            foreach (var clause in _methodBody.ExceptionHandlingClauses)
            {
                // Map IL offsets to exception handling regions

                switch (clause.Flags)
                {

                    case ExceptionHandlingClauseOptions.Clause:
                        Console.WriteLine(clause.TryOffset);
                        // Try block
                        //for (int offset = clause.TryOffset; offset < clause.TryOffset + clause.TryLength; offset++)
                        //{
                        //    //_exceptionHandlerContext[offset] = "try block";
                        //    Console.WriteLine("try");
                        //}
                        // Catch block
                        for (int offset = clause.HandlerOffset; offset < clause.HandlerOffset + clause.HandlerLength; offset++)
                        {
                            // _exceptionHandlerContext[offset] = "catch block";
                            Console.WriteLine("catch");
                        }
                        break;

                    case ExceptionHandlingClauseOptions.Finally:
                        // Try block
                        //for (int offset = clause.TryOffset; offset < clause.TryOffset + clause.TryLength; offset++)
                        //{
                        //    //if (!_exceptionHandlerContext.ContainsKey(offset))
                        //    // _exceptionHandlerContext[offset] = "try block";
                        //    Console.WriteLine("try");
                        //}

                        // Finally block
                        for (int offset = clause.HandlerOffset; offset < clause.HandlerOffset + clause.HandlerLength; offset++)
                        {
                            //_exceptionHandlerContext[offset] = "finally block";
                            Console.WriteLine("finally");
                        }
                        break;

                    case ExceptionHandlingClauseOptions.Fault:
                        // Try block
                        //for (int offset = clause.TryOffset; offset < clause.TryOffset + clause.TryLength; offset++)
                        //{
                        //    //if (!_exceptionHandlerContext.ContainsKey(offset))
                        //    // _exceptionHandlerContext[offset] = "try block";
                        //    Console.WriteLine("try");
                        //}
                        // Fault block
                        for (int offset = clause.HandlerOffset; offset < clause.HandlerOffset + clause.HandlerLength; offset++)
                        {
                            //_exceptionHandlerContext[offset] = "fault block";
                            Console.WriteLine("fault");
                        }
                        break;

                    case ExceptionHandlingClauseOptions.Filter:
                        //// Try block
                        //for (int offset = clause.TryOffset; offset < clause.TryOffset + clause.TryLength; offset++)
                        //{
                        //    //if (!_exceptionHandlerContext.ContainsKey(offset))
                        //    //    _exceptionHandlerContext[offset] = "try block";
                        //    Console.WriteLine("try");
                        //}
                        // Filter block
                        if (clause.FilterOffset > 0)
                        {
                            for (int offset = clause.FilterOffset; offset < clause.HandlerOffset; offset++)
                            {
                                //_exceptionHandlerContext[offset] = "filter block";
                                Console.WriteLine("filter");
                            }
                        }
                        // Handler block for filter
                        for (int offset = clause.HandlerOffset; offset < clause.HandlerOffset + clause.HandlerLength; offset++)
                        {
                            //_exceptionHandlerContext[offset] = "filter handler block";
                            Console.WriteLine("filter handler");
                        }
                        break;
                }
            }
            return string.Empty;
        }

        public static Dictionary<string, string> analysedMethods = new Dictionary<string, string>();

        private static int GetOperandSize(OpCode opCode)
        {
            switch (opCode.OperandType)
            {
                case System.Reflection.Emit.OperandType.InlineNone:
                    return 0;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar:
                    return 1;
                case System.Reflection.Emit.OperandType.InlineVar:
                    return 2;
                case System.Reflection.Emit.OperandType.InlineBrTarget:
                case System.Reflection.Emit.OperandType.InlineField:
                case System.Reflection.Emit.OperandType.InlineI:
                case System.Reflection.Emit.OperandType.InlineMethod:
                case System.Reflection.Emit.OperandType.InlineSig:
                case System.Reflection.Emit.OperandType.InlineString:
                case System.Reflection.Emit.OperandType.InlineSwitch:
                case System.Reflection.Emit.OperandType.InlineTok:
                case System.Reflection.Emit.OperandType.InlineType:
                case System.Reflection.Emit.OperandType.ShortInlineR:
                    return 4;
                case System.Reflection.Emit.OperandType.InlineI8:
                case System.Reflection.Emit.OperandType.InlineR:
                    return 8;
                default:
                    return 0;
            }
        }

        public static void PrintAllILInstructions(MethodInfo method)
        {
            if(method == null)
            {
                Console.WriteLine("Method is null.");
                return;
            }
            EnsureInitialized();

            var body = method.GetMethodBody();
            AnalyzeExceptionHandlers(body);

            if (body == null)
            {
                Console.WriteLine("No method body found.");
                return;
            }

            byte[] il = body.GetILAsByteArray();
            if (il == null || il.Length == 0)
            {
                Console.WriteLine("No IL code found.");
                return;
            }

            Module module = method.Module;
            int pos = 0;
            while (pos < il.Length)
            {
                int offset = pos;
                OpCode opcode;
                byte op1 = il[pos++];

                if (op1 != 0xFE)
                {
                    opcode = singleByteOpCodes[op1];
                }
                else
                {
                    if (pos >= il.Length)
                    {
                        Console.WriteLine($"Unexpected end of IL at offset {offset:X4}");
                        break;
                    }
                    byte op2 = il[pos++];
                    opcode = multiByteOpCodes[op2];
                }

                if (opcode.Name == null)
                {
                    Console.WriteLine($"Unknown opcode at offset {offset:X4}: 0x{op1:X2}");
                    continue;
                }

                Console.Write($"IL_{offset:X4}: {opcode.Name}: {opcode.OperandType}: {GetOperandSize(opcode)}");
                DetectConditionalBlocks(opcode, offset, GetOperandSize(opcode));
                // Handle operand for call/callvirt/newobj
                if (opcode.OperandType == OperandType.InlineMethod)
                {
                    int token = BitConverter.ToInt32(il, pos);
                    pos += 4;
                    try
                    {
                        MethodBase calledMethod = module.ResolveMethod(token);
                        if (analysedMethods.ContainsKey($" -> {calledMethod.DeclaringType.FullName}.{calledMethod.Name}"))
                        //{
                        //    Console.WriteLine($" [Already analysed]");
                        //    return;
                        //}
                        
                        Console.Write($" -> {calledMethod.DeclaringType.FullName}.{calledMethod.Name}");
                        //PrintAllILInstructions(Type.GetType($"{calledMethod.DeclaringType.FullName}").GetMethod($"{calledMethod.Name}", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                        analysedMethods[$" -> {calledMethod.DeclaringType.FullName}.{calledMethod.Name}"] = calledMethod.ToString();

                    }
                    catch
                    {
                        Console.Write($" -> [Could not resolve method, token=0x{token:X8}]");
                    }
                }
                // (Optional: handle other operand types here)

                Console.WriteLine();
            }
        }


    }

    public static class Logger
    {
        public enum OutputTarget
        {
            Console,
            File,
            Both
        }

        private static string _logFilePath = "output.log";

        /// <summary>
        /// Sets the file path for logging output
        /// </summary>
        /// <param name="filePath">Path to the log file</param>
        public static void SetLogFile(string filePath)
        {
            _logFilePath = filePath;
        }

        /// <summary>
        /// Prints a message to the specified output target(s)
        /// </summary>
        /// <param name="message">Message to print</param>
        /// <param name="target">Where to output the message</param>
        public static void Print(string message, OutputTarget target = OutputTarget.Both)
        {
            switch (target)
            {
                case OutputTarget.Console:
                    Console.WriteLine(message);
                    break;
                case OutputTarget.File:
                    WriteToFile(message);
                    break;
                case OutputTarget.Both:
                    Console.WriteLine(message);
                    WriteToFile(message);
                    break;
            }
        }

        /// <summary>
        /// Prints a formatted message to the specified output target(s)
        /// </summary>
        /// <param name="format">Format string</param>
        /// <param name="target">Where to output the message</param>
        /// <param name="args">Arguments for formatting</param>
        public static void PrintFormat(string format, OutputTarget target, params object[] args)
        {
            string message = string.Format(format, args);
            Print(message, target);
        }

        /// <summary>
        /// Prints a timestamped message to the specified output target(s)
        /// </summary>
        /// <param name="message">Message to print</param>
        /// <param name="target">Where to output the message</param>
        public static void PrintWithTimestamp(string message, OutputTarget target = OutputTarget.Console)
        {
            string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Print(timestampedMessage, target);
        }

        private static void WriteToFile(string message)
        {
            try
            {
                // Append to file, create if it doesn't exist
                using (StreamWriter writer = new StreamWriter(_logFilePath, append: true))
                {
                    writer.WriteLine(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to file: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the log file
        /// </summary>
        public static void ClearLogFile()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    File.WriteAllText(_logFilePath, string.Empty);
                }
            }
            catch (Exception ex)
            {
                Logger.Print($"Error clearing log file: {ex.Message}");
            }
        }
    }

    public class Detail
    {


public static void PrintMethodIL(MethodInfo method)
    {
        var methodBody = method.GetMethodBody();
        if (methodBody == null)
        {
            Console.WriteLine("No method body found.");
            return;
        }

        var il = methodBody.GetILAsByteArray();
        var module = method.Module;
        int position = 0;

        while (position < il.Length)
        {
            
            int offset = position;
            OpCode opcode;
            byte value = il[position++];

                if (il[position] == 0xFE && position + 1 < il.Length)
                {
                    // Two-byte opcode
                    opcode = Program.GetTwoByteOpCode(il[position + 1]);
                    //opCodeSize = 2;
                }
                else
                {
                    // Single-byte opcode
                    opcode = Program.GetSingleByteOpCode(il[position]);
                    //opCodeSize = 1;
                }

                Console.Write($"IL_{offset:X4}: {opcode.Name,-10}");

            // Read operand (if any)
            int operandSize = GetOperandSize(opcode.OperandType);
            object operand = null;
            if (operandSize > 0)
            {
                operand = ReadOperand(il, ref position, opcode.OperandType, module, method);
                Console.Write($" {operand}");
            }

            Console.WriteLine();
        }
    }

    // Helper dictionaries for opcodes
    private static readonly OpCode[] singleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] multiByteOpCodes = new OpCode[0x100];

    //static PrintMethodIL()
    //{
    //    foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
    //    {
    //        if (fi.GetValue(null) is OpCode op)
    //        {
    //            if (op.Size == 1)
    //                singleByteOpCodes[op.Value & 0xFF] = op;
    //            else
    //                multiByteOpCodes[op.Value & 0xFF] = op;
    //        }
    //    }
    //}

    private static int GetOperandSize(OperandType type)
    {
        switch (type)
        {
            case OperandType.InlineNone: return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar: return 1;
            case OperandType.InlineVar: return 2;
            case OperandType.InlineI:
            case OperandType.InlineBrTarget:
            case OperandType.InlineR:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType: return 4;
            case OperandType.InlineI8:
            case OperandType.InlineSwitch: return -1; // Special handling
            default: throw new NotSupportedException();
        }
    }

    private static object ReadOperand(byte[] il, ref int position, OperandType type, Module module, MethodInfo method)
    {
        switch (type)
        {
            case OperandType.InlineNone:
                return null;
            case OperandType.ShortInlineI:
                return (sbyte)il[position++];
            case OperandType.ShortInlineVar:
                return il[position++];
            case OperandType.InlineVar:
                return BitConverter.ToUInt16(il, position += 2);
            case OperandType.InlineI:
                int int32 = BitConverter.ToInt32(il, position);
                position += 4;
                return int32;
            case OperandType.InlineI8:
                long int64 = BitConverter.ToInt64(il, position);
                position += 8;
                return int64;
            case OperandType.ShortInlineR:
                float f = BitConverter.ToSingle(il, position);
                position += 4;
                return f;
            case OperandType.InlineR:
                double d = BitConverter.ToDouble(il, position);
                position += 8;
                return d;
            case OperandType.InlineString:
                    int metaToken = BitConverter.ToInt32(il, position);
                    position += 4;
                    return null;
                //return module.ResolveString(metaToken);
                case OperandType.InlineMethod:
            case OperandType.InlineField:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            case OperandType.InlineSig:
                int token = BitConverter.ToInt32(il, position);
                position += 4;
                return $"token: 0x{token:X8}";
            case OperandType.ShortInlineBrTarget:
                sbyte rel8 = (sbyte)il[position++];
                //return $"IL_{(position + rel8):X4}";
                    return null;
                case OperandType.InlineBrTarget:
                    int rel32 = BitConverter.ToInt32(il, position);
                    position += 4;
                    return null;
                    //return $"IL_{(position + rel32):X4}";
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, position);
                    position += 4;
                    int[] targets = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        targets[i] = BitConverter.ToInt32(il, position);
                        position += 4;
                    }
                    //return string.Join(", ", targets.Select(t => $"IL_{(position + t):X4}"));
                    return null;
                default:
                return null;
        }
    }
}
    
    internal class Program
    {
        //static void Main(string[] args)
        //{
        //    Console.WriteLine("Hello, World!");

        //    //ParseILAndAnalyzeMethodCallsAutoDiscovery("Q:\\Personal\\Lynx\\Lynx\\bin\\Debug\\net8.0\\Lynx.dll", "Lynx.Program", "Main");
        //    Detail.PrintMethodIL(typeof(Program).GetMethod("GetMethod"));

        //   var method = typeof(Detail).GetMethod("GetMethod");
        //    var body = method.GetMethodBody();
        //    var ehClauses = body.ExceptionHandlingClauses;

        //    foreach (var clause in ehClauses)
        //    {
        //        Console.WriteLine($"Clause Type: {clause.Flags}");
        //        Console.WriteLine($"Try Offset: {clause.TryOffset}, Length: {clause.TryLength}");
        //        Console.WriteLine($"Handler Offset: {clause.HandlerOffset}, Length: {clause.HandlerLength}");
        //        if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
        //            Console.WriteLine($"Catch Type: {clause.CatchType}");
        //    }

        //    //
        //}

        

        public static void Main()
        {
            // Get the OpcCodes using Reflection
            //Type opCodes = typeof(OpCodes);
            //var opCodesList = opCodes.GetFields(BindingFlags.Public | BindingFlags.Static)
            //.Where(f => f.FieldType == typeof(OpCode))
            //.Select(f =>
            //{
            //    OpCode opCode = (OpCode)f.GetValue(null);
            //    return new { Name = f.Name, Instruction = opCode.Name, Size = opCode.Size, OpCode = string.Format("0x{0:X2}", opCode.Value) };
            //});

            //// Print to the Console
            //var opCodesStrings = opCodesList
            //.Select(o => string.Format("{0,-10}{1,-15}{2,-10}{3,-10}", o.Name, o.Instruction, o.Size, o.OpCode))
            //.ToList();
            //opCodesStrings.Insert(0, string.Format("{0,-10}{1,-15}{2,-10}{3,-10}", "Name", "Instruction", "Size", "OpCode"));
            //opCodesStrings.Insert(1, string.Format("{0,-10}{1,-15}{2,-10}{3,-10}", "----", "-----------", "----", "------"));
            //opCodesStrings.ForEach(Console.WriteLine);
            ILPrinter.PrintAllILInstructions(typeof(Program).GetMethod("TestMethod", BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic));
            //PrintAllILInstructions(typeof(Program).GetMethod("Main"));
        }
        public static void PrintAllILInstructions(MethodInfo method)
        {
            var body = method.GetMethodBody();
            if (body == null)
            {
                Console.WriteLine("No method body found.");
                return;
            }

            byte[] il = body.GetILAsByteArray();
            if (il == null || il.Length == 0)
            {
                Console.WriteLine("No IL code found.");
                return;
            }

            // Build opcode lookup tables
            OpCode[] singleByteOpCodes = new OpCode[0x100];
            OpCode[] multiByteOpCodes = new OpCode[0x100];
            foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (fi.GetValue(null) is OpCode op)
                {
                    if (op.Size == 1)
                        singleByteOpCodes[op.Value & 0xFF] = op;
                    else
                        multiByteOpCodes[op.Value & 0xFF] = op;
                }
            }

            int pos = 0;
            while (pos < il.Length)
            {
                int offset = pos;
                OpCode opcode;
                byte op1 = il[pos++];

                if (op1 != 0xFE)
                {
                    opcode = singleByteOpCodes[op1];
                }
                else
                {
                    byte op2 = il[pos++];
                    opcode = multiByteOpCodes[op2];
                }

                Console.WriteLine($"IL_{offset:X4}: {opcode.Name}");
                // (Optional: You can add operand reading here if needed)
            }
        }
        public static ILOpCode ReadOpCode(BlobReader reader)
        {
            byte b = reader.ReadByte();
            if (b != 0xFE)
            {
                return (ILOpCode)b;
            }
            else
            {
                // Multi-byte opcode
                byte b2 = reader.ReadByte();
                return (ILOpCode)(0x100 | b2);
            }
        }

        public void TestMethod()
        {
            try
            {
                Console.WriteLine("In try block");
                try
                {

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Caught exception: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("In inner finally block");
                }
            }
            catch
            {
                               Console.WriteLine("In catch block");
            }
            
        }

        

        public void GetMethod()
        {
            Console.WriteLine("This is a method in the Program class.");
            Console.WriteLine("It will be analyzed for method calls and IL parsing.");
            try
            {

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Caught exception: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("Finally block executed.");
            }
        }

        public static void ParseILAndAnalyzeMethodCallsAutoDiscovery(string startingAssemblyPath, string className, string methodName)
        {
            var analyzedMethods = new HashSet<string>();
            var loadedAssemblies = new Dictionary<string, Assembly>();
            var assemblySearchPaths = new HashSet<string>();

            // Add the directory of the starting assembly as a search path
            assemblySearchPaths.Add(Path.GetDirectoryName(startingAssemblyPath));

            Logger.Print("=== Lazy-Loading Cross-Assembly Analysis ===\n");
            Logger.Print($"Starting with assembly: {Path.GetFileName(startingAssemblyPath)}");
            Logger.Print($"Target: {className}.{methodName}");
            Logger.Print("Note: Assemblies will be loaded only when referenced by method calls");
            Logger.Print("");

            // Load only the starting assembly
            try
            {
                Assembly startingAssembly = Assembly.LoadFrom(startingAssemblyPath);
                loadedAssemblies[startingAssembly.GetName().Name] = startingAssembly;
                Logger.Print($"✓ Loaded starting assembly: {startingAssembly.GetName().Name}");

                // Add search paths for potential dependencies (but don't load them yet)
                AddPotentialSearchPaths(startingAssemblyPath, assemblySearchPaths);
            }
            catch (Exception ex)
            {
                Logger.Print($"✗ Failed to load starting assembly: {ex.Message}");
                return;
            }

            Logger.Print("\n=== Method Call Tree ===");
            Logger.Print("Legend:");
            Logger.Print("├── Method Name [Assembly Info]");
            Logger.Print("│   └─ Method Details");
            Logger.Print("│       └─ Called Method [Assembly Type]");
            Logger.Print("");

            // Start recursive analysis with lazy assembly loading
            ParseILWithAutoAssemblyLoading(startingAssemblyPath, className, methodName,
                                          loadedAssemblies, analyzedMethods, assemblySearchPaths, 0);

            Logger.Print($"\n=== Lazy-Loading Summary ===");
            Logger.Print($"Total assemblies loaded on-demand: {loadedAssemblies.Count}");
            Logger.Print($"Total methods analyzed: {analyzedMethods.Count}");
            Logger.Print($"Search paths available: {assemblySearchPaths.Count}");
            Logger.Print("Note: Only assemblies actually referenced by method calls were loaded");

            Logger.Print("\nLazy-loaded assemblies:");
            foreach (var assembly in loadedAssemblies.OrderBy(kvp => kvp.Key))
            {
                Logger.Print($"  - {assembly.Key}: {assembly.Value.Location}");
            }
        }

        // Get or load assembly with auto-discovery
        private static Assembly GetOrLoadAssembly(string assemblyPath, Dictionary<string, Assembly> loadedAssemblies,
                                                HashSet<string> assemblySearchPaths)
        {
            try
            {
                string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

                // Check if already loaded by name first
                if (loadedAssemblies.ContainsKey(assemblyName))
                    return loadedAssemblies[assemblyName];

                // Also check if any loaded assembly has the same location to prevent duplicate loading
                foreach (var kvp in loadedAssemblies)
                {
                    if (!string.IsNullOrEmpty(kvp.Value.Location) &&
                        Path.GetFullPath(kvp.Value.Location).Equals(Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value;
                    }
                }

                // Try to load from the specified path
                if (File.Exists(assemblyPath))
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyPath);
                    string actualAssemblyName = assembly.GetName().Name;

                    // Check if we already have this assembly loaded under a different name
                    if (loadedAssemblies.ContainsKey(actualAssemblyName))
                    {
                        return loadedAssemblies[actualAssemblyName];
                    }

                    loadedAssemblies[actualAssemblyName] = assembly;
                    Logger.Print($"✓ Auto-loaded assembly: {actualAssemblyName}");
                    return assembly;
                }

                // Search in discovered paths
                foreach (string searchPath in assemblySearchPaths)
                {
                    string potentialPath = Path.Combine(searchPath, assemblyName + ".dll");
                    if (File.Exists(potentialPath))
                    {
                        Assembly assembly = Assembly.LoadFrom(potentialPath);
                        string actualAssemblyName = assembly.GetName().Name;

                        // Check if we already have this assembly loaded
                        if (loadedAssemblies.ContainsKey(actualAssemblyName))
                        {
                            return loadedAssemblies[actualAssemblyName];
                        }

                        loadedAssemblies[actualAssemblyName] = assembly;
                        Logger.Print($"✓ Auto-discovered and loaded: {actualAssemblyName} from {potentialPath}");
                        return assembly;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Print($"Failed to load assembly {assemblyPath}: {ex.Message}");
                return null;
            }
        }

        // Find type in any loaded assembly
        private static Type FindTypeInLoadedAssemblies(string typeName, Dictionary<string, Assembly> loadedAssemblies)
        {
            return FindTypeInLoadedAssemblies(typeName, loadedAssemblies, 0, true);
        }

        // Find type in any loaded assembly with indentation support
        private static Type FindTypeInLoadedAssemblies(string typeName, Dictionary<string, Assembly> loadedAssemblies, int depth)
        {
            return FindTypeInLoadedAssemblies(typeName, loadedAssemblies, depth, false);
        }

        // Find type in any loaded assembly with indentation and verbosity control
        private static Type FindTypeInLoadedAssemblies(string typeName, Dictionary<string, Assembly> loadedAssemblies, int depth, bool verbose)
        {
            string indent = new string(' ', depth * 4);

            foreach (var kvp in loadedAssemblies)
            {
                try
                {
                    Type type = kvp.Value.GetType(typeName);
                    if (type != null)
                    {
                        if (verbose)
                            Logger.Print($"{indent}    └─ Found type '{typeName}' in assembly '{kvp.Key}'");
                        return type;
                    }

                    // Also try to find by simple name if full name doesn't work
                    Type[] allTypes = kvp.Value.GetTypes();
                    Type typeBySimpleName = allTypes.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
                    if (typeBySimpleName != null)
                    {
                        if (verbose)
                            Logger.Print($"{indent}    └─ Found type '{typeName}' by simple name search in assembly '{kvp.Key}'");
                        return typeBySimpleName;
                    }
                }
                catch (Exception ex)
                {
                    if (verbose)
                        Logger.Print($"{indent}    └─ Error searching for type '{typeName}' in assembly '{kvp.Key}': {ex.Message}");
                }
            }

            if (verbose)
            {
                Logger.Print($"{indent}    └─ Type '{typeName}' not found in any loaded assembly");
                Logger.Print($"{indent}    └─ Searched in assemblies: {string.Join(", ", loadedAssemblies.Keys)}");
            }
            return null;
        }

        // Get two-byte opcode
        public static OpCode GetTwoByteOpCode(byte value)
        {
            short opCodeValue = (short)(0xFE00 | value);
            var field = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(f => f.FieldType == typeof(OpCode) &&
                               ((OpCode)f.GetValue(null)).Size == 2 &&
                               ((OpCode)f.GetValue(null)).Value == opCodeValue);

            return field != null ? (OpCode)field.GetValue(null) : OpCodes.Nop;
        }

        // Get single-byte opcode
        public static OpCode GetSingleByteOpCode(byte value)
        {
            var field = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(f => f.FieldType == typeof(OpCode) &&
                               ((OpCode)f.GetValue(null)).Size == 1 &&
                               ((OpCode)f.GetValue(null)).Value == value);

            return field != null ? (OpCode)field.GetValue(null) : OpCodes.Nop;
        }

        // Check if opcode represents a method call
        private static bool IsMethodCallOpCode(OpCode opCode)
        {
            return opCode.Value == OpCodes.Call.Value ||
                   opCode.Value == OpCodes.Callvirt.Value ||
                   opCode.Value == OpCodes.Newobj.Value ||
                   opCode.Value == OpCodes.Calli.Value;
        }

        // Get operand size for opcode
        private static int GetOperandSize(OpCode opCode)
        {
            switch (opCode.OperandType)
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
                case OperandType.InlineSwitch:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                default:
                    return 0;
            }
        }

        // Parse IL bytecode to extract method calls
        private static List<MethodBase> ParseILForMethodCalls(byte[] il, Module module)
        {
            var calledMethods = new List<MethodBase>();

            for (int i = 0; i < il.Length; i++)
            {
                OpCode opCode;
                int opCodeSize;

                // Parse single-byte or two-byte opcodes
                if (il[i] == 0xFE && i + 1 < il.Length)
                {
                    // Two-byte opcode
                    opCode = GetTwoByteOpCode(il[i + 1]);
                    opCodeSize = 2;
                }
                else
                {
                    // Single-byte opcode
                    opCode = GetSingleByteOpCode(il[i]);
                    opCodeSize = 1;
                }

                // Check if this opcode represents a method call
                if (IsMethodCallOpCode(opCode))
                {
                    try
                    {
                        // Extract the metadata token (next 4 bytes after opcode)
                        if (i + opCodeSize + 4 <= il.Length)
                        {
                            int token = BitConverter.ToInt32(il, i + opCodeSize);
                            MethodBase method = module.ResolveMethod(token);

                            if (method != null)
                            {
                                calledMethods.Add(method);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Token resolution failed, continue parsing
                    }
                }

                // Skip operand bytes
                i += opCodeSize + GetOperandSize(opCode) - 1;
            }

            // Return all method calls including duplicates to show all occurrences in source code
            return calledMethods;
        }

        // Helper method to get a readable method signature
        private static string GetMethodSignature(MethodInfo method)
        {
            var parameters = method.GetParameters()
                .Select(p => $"{p.ParameterType.Name} {p.Name}")
                .ToArray();
            string paramString = string.Join(", ", parameters);
            return $"{method.ReturnType.Name} {method.Name}({paramString})";
        }

        // Parse IL with automatic assembly loading
        private static void ParseILWithAutoAssemblyLoading(string assemblyPath, string className, string methodName,
                                                          Dictionary<string, Assembly> loadedAssemblies,
                                                          HashSet<string> analyzedMethods,
                                                          HashSet<string> assemblySearchPaths,
                                                          int depth = 0)
        {
            try
            {
                string indent = new string(' ', depth * 4);
                // Limit recursion depth to prevent infinite loops and overwhelming output
                if (depth > 20)
                {

                    Logger.Print($"{indent}├── [Max recursion depth reached - stopping here]");
                    return;
                }

                // Create tree indentation
                string treeChar = depth == 0 ? "" : "└── ";
                string baseIndent = new string(' ', depth * 4);
                // For recursive calls (depth > 0), we need to align with the call site
                if (depth > 0)
                {
                    baseIndent = new string(' ', depth * 12); // Each level adds 12 spaces for proper alignment
                }

                Assembly assembly = GetOrLoadAssembly(assemblyPath, loadedAssemblies, assemblySearchPaths);
                if (assembly == null) return;

                Type targetType = FindTypeInLoadedAssemblies(className, loadedAssemblies, depth, false);
                if (targetType == null)
                {
                    Logger.Print($"{baseIndent}Class '{className}' not found in any loaded assembly.");
                    Logger.Print($"{baseIndent}Available loaded assemblies: {string.Join(", ", loadedAssemblies.Keys)}");
                    return;
                }

                // Try to find the method - handle overloads by getting all methods with the name
                MethodInfo targetMethod = targetType.GetMethod(methodName);
                if (targetMethod == null)
                {
                    // Try to find any method with this name (in case of overloads)
                    var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Instance | BindingFlags.Static)
                                           .Where(m => m.Name == methodName)
                                           .ToArray();

                    if (methods.Length > 0)
                    {
                        targetMethod = methods[0]; // Use the first overload found
                        // Don't print the overload message here - it will be printed after the method signature
                    }
                    else
                    {
                        Logger.Print($"{baseIndent}Method '{methodName}' not found in class '{className}'.");
                        var availableMethods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                                   BindingFlags.Instance | BindingFlags.Static)
                                                        .Where(m => m.DeclaringType == targetType)
                                                        .Select(m => m.Name)
                                                        .Distinct()
                                                        .Take(10)  // Limit to first 10 to avoid overwhelming output
                                                        .ToArray();
                        Logger.Print($"{baseIndent}Available methods in {className}: {string.Join(", ", availableMethods)}{(availableMethods.Length >= 10 ? "..." : "")}");
                        return;
                    }
                }

                string methodSignature = $"{className}.{methodName}";
                if (analyzedMethods.Contains(methodSignature))
                {
                    if (depth == 0)
                    {
                        Logger.Print($"{baseIndent}{treeChar}{methodSignature} (already analyzed - circular reference)");
                    }
                    return;
                }

                analyzedMethods.Add(methodSignature);

                // Only print the method signature for the root method (depth == 0)
                // For recursive calls (depth > 0), the method was already shown as a sibling call
                if (depth == 0)
                {
                    Logger.Print($"{baseIndent}{treeChar}{methodSignature}");
                    Logger.Print($"{baseIndent}│   └─ Assembly: {targetType.Assembly.GetName().Name}");
                }

                // Print overload information if multiple overloads were found
                if (targetMethod != null && depth == 0)
                {
                    var allMethodsWithSameName = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                                      BindingFlags.Instance | BindingFlags.Static)
                                                          .Where(m => m.Name == methodName)
                                                          .ToArray();
                    if (allMethodsWithSameName.Length > 1)
                    {
                        Logger.Print($"{baseIndent}│   └─ Found {allMethodsWithSameName.Length} method overload(s) for '{methodName}', using: {GetMethodSignature(targetMethod)}");
                    }
                }

                var methodBody = targetMethod.GetMethodBody();
                if (methodBody == null)
                {
                    if (depth == 0)
                    {
                        Logger.Print($"{baseIndent}│   └─ Method has no IL body (likely abstract or interface method)");
                    }
                    return;
                }

                byte[] il = methodBody.GetILAsByteArray();
                var calledMethods = ParseILForMethodCalls(il, targetMethod.Module);

                // Check if this is an async method and also analyze the generated state machine
                bool isAsyncMethod = IsAsyncMethod(targetMethod);
                if (isAsyncMethod && depth == 0)
                {
                    Logger.Print($"{baseIndent}│   └─ ⚡ Async method detected - will also analyze state machine");

                    // Find and analyze the state machine's MoveNext method
                    var stateMachineType = FindAsyncStateMachineType(targetType, methodName);
                    if (stateMachineType != null)
                    {
                        Logger.Print($"{baseIndent}│   └─ 🔧 Found state machine: {stateMachineType.Name}");

                        // Recursively analyze the MoveNext method of the state machine
                        ParseILWithAutoAssemblyLoading(assembly.Location,
                                                     stateMachineType.FullName,
                                                     "MoveNext",
                                                     loadedAssemblies, analyzedMethods, assemblySearchPaths, depth + 1);
                    }
                }

                // Show IL details for all methods, but with different formatting based on depth
                if (depth == 0)
                {
                    // Root method: show full details
                    Logger.Print($"{baseIndent}│   └─ IL Code Size: {il.Length} bytes");

                    if (calledMethods.Count == 0)
                    {
                        Logger.Print($"{baseIndent}│   └─ No method calls found");
                        return;
                    }

                    Logger.Print($"{baseIndent}│   └─ Method calls ({calledMethods.Count}):");
                }
                else
                {
                    // Recursive calls: show assembly and IL info without the method name header
                    Logger.Print($"{baseIndent}│   └─ Assembly: {targetType.Assembly.GetName().Name}");
                    Logger.Print($"{baseIndent}│   └─ IL Code Size: {il.Length} bytes");

                    if (calledMethods.Count == 0)
                    {
                        Logger.Print($"{baseIndent}│   └─ No method calls found");
                        return;
                    }

                    Logger.Print($"{baseIndent}│   └─ Method calls ({calledMethods.Count}):");
                }

                foreach (var calledMethod in calledMethods)
                {
                    if (calledMethod.DeclaringType == null) continue;

                    string calledMethodSignature = $"{calledMethod.DeclaringType.FullName}.{calledMethod.Name}";
                    string calledAssemblyName = calledMethod.DeclaringType.Assembly.GetName().Name;
                    string currentAssemblyName = assembly.GetName().Name;

                    // Show all method calls as siblings at the same level
                    if (calledMethod.DeclaringType == typeof(object) ||
                        calledMethod.DeclaringType.FullName.StartsWith("System."))
                    {
                        Logger.Print($"{baseIndent}│       └─ {calledMethodSignature} [System - skipped]");
                        continue;
                    }

                    // For non-system methods, show them as siblings first, then recurse
                    Logger.Print($"{baseIndent}│       └─ {calledMethodSignature} [Assembly: {calledAssemblyName}]");

                    // Check if this is truly a cross-assembly call
                    if (calledAssemblyName != currentAssemblyName)
                    {
                        // This is a cross-assembly call - try to auto-load the assembly
                        Assembly calledAssembly = AutoLoadAssemblyForType(calledMethod.DeclaringType,
                                                                         loadedAssemblies, assemblySearchPaths);

                        if (calledAssembly != null)
                        {
                            // Cross-assembly call - recursively analyze the called method with increased depth
                            ParseILWithAutoAssemblyLoading(calledAssembly.Location,
                                                         calledMethod.DeclaringType.FullName,
                                                         calledMethod.Name,
                                                         loadedAssemblies, analyzedMethods, assemblySearchPaths, depth + 1);
                        }
                    }
                    else
                    {
                        // This is a same-assembly call - recursively analyze
                        ParseILWithAutoAssemblyLoading(assembly.Location,
                                                     calledMethod.DeclaringType.FullName,
                                                     calledMethod.Name,
                                                     loadedAssemblies, analyzedMethods, assemblySearchPaths, depth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                string baseIndent = new string(' ', depth * 4);
                if (depth > 0)
                {
                    baseIndent = new string(' ', depth * 12);
                }
                Logger.Print($"{baseIndent}Error in auto-analysis: {ex.Message}");
            }
        }

        // Auto-load assembly for a specific type (lazy loading approach)
        private static Assembly AutoLoadAssemblyForType(Type type, Dictionary<string, Assembly> loadedAssemblies,
                                                      HashSet<string> assemblySearchPaths)
        {
            try
            {
                string assemblyName = type.Assembly.GetName().Name;

                // Check if already loaded
                if (loadedAssemblies.ContainsKey(assemblyName))
                    return loadedAssemblies[assemblyName];

                // Try to get assembly location from the type itself (most reliable)
                string assemblyLocation = type.Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation) && File.Exists(assemblyLocation))
                {
                    loadedAssemblies[assemblyName] = type.Assembly;
                    Logger.Print($"✓ Lazy-loaded existing assembly: {assemblyName}");
                    return type.Assembly;
                }

                // Search for the assembly in discovered paths (lazy discovery)
                foreach (string searchPath in assemblySearchPaths)
                {
                    string potentialPath = Path.Combine(searchPath, assemblyName + ".dll");
                    if (File.Exists(potentialPath))
                    {
                        Assembly assembly = Assembly.LoadFrom(potentialPath);
                        loadedAssemblies[assemblyName] = assembly;
                        Logger.Print($"✓ Lazy-loaded assembly: {assemblyName} from {potentialPath}");
                        return assembly;
                    }
                }

                // Try to load by name (GAC or runtime)
                try
                {
                    Assembly assembly = Assembly.Load(type.Assembly.GetName());
                    loadedAssemblies[assemblyName] = assembly;
                    Logger.Print($"✓ Lazy-loaded from GAC/Runtime: {assemblyName}");
                    return assembly;
                }
                catch
                {
                    // Failed to load from GAC
                    Logger.Print($"⚠ Could not lazy-load assembly: {assemblyName}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Print($"Failed to lazy-load assembly for type {type.FullName}: {ex.Message}");
                return null;
            }
        }

        // Find the async state machine type for an async method
        private static Type FindAsyncStateMachineType(Type containingType, string methodName)
        {
            try
            {
                // Look for nested types that are async state machines
                var nestedTypes = containingType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);

                foreach (var nestedType in nestedTypes)
                {
                    // State machine types are usually named like <MethodName>d__X
                    if (nestedType.Name.Contains($"<{methodName}>") && nestedType.Name.Contains("d__"))
                    {
                        // Verify it has MoveNext method
                        var moveNextMethod = nestedType.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (moveNextMethod != null)
                        {
                            return nestedType;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If we can't find the state machine, just continue without it
            }

            return null;
        }

        // Check if a method is async
        private static bool IsAsyncMethod(MethodInfo method)
        {
            // Check if the method has AsyncStateMachineAttribute
            return method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>() != null;
        }

        // Add potential search paths without loading assemblies (lazy approach)
        private static void AddPotentialSearchPaths(string assemblyPath, HashSet<string> searchPaths)
        {
            try
            {
                // Add common search paths without actually loading dependencies
                string assemblyDir = Path.GetDirectoryName(assemblyPath);

                // Add current assembly directory and common subdirectories
                searchPaths.Add(assemblyDir);
                searchPaths.Add(Path.Combine(assemblyDir, "bin"));
                searchPaths.Add(Path.Combine(assemblyDir, "lib"));

                // Add common .NET runtime paths
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                               "dotnet", "shared", "Microsoft.NETCore.App"));
                searchPaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                               "Reference Assemblies", "Microsoft", "Framework", ".NETFramework"));

                Logger.Print($"✓ Added {searchPaths.Count} potential search paths for lazy loading");
            }
            catch (Exception ex)
            {
                Logger.Print($"Warning: Could not add potential search paths: {ex.Message}");
            }
        }
    }
}
