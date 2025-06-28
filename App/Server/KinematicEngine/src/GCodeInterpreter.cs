using System;
using System.IO;
using System.Text;
using System.Threading;

namespace KinematicEngine
{
    public partial class RS274NGC
    {
        // --- Callback delegate definitions ---
        public delegate void GCompleteCallback(int status, int lineNo, int sequenceNumber, string errorMessage);
        public delegate void GStatusCallback(int lineNo, string message);
        public delegate int GUserCallback(string message);
        public delegate int GMUserCallback(int mCode);
        public delegate int GScreenScriptCallback(string fileName);
        public const int MAX_MCODE_DOUBLE_PARAMS = 16;
        public const int MAX_MCODE_ACTIONS = 64;


        // Interpreter entrypoints (add real logic or P/Invoke)
        public static int ReadSetup(string filename) => RS274NGC_OK;
        public static int ReadVars(string filename)  => RS274NGC_OK;
        public static int ReadGeo(string filename)   => RS274NGC_OK;
        public static int ReadTool(string filename)  => RS274NGC_OK;
        public static string GetLastMessage()          => string.Empty;

        public static int DownloadInit()                           => RS274NGC_OK;
        public static int DownloadFinish()                         => RS274NGC_OK;
        public static int AbortFlag()                              => RS274NGC_OK;
        public static int ResumeSafe()                             => RS274NGC_OK;

        public static void SetFeedRate(double rate) { }
        public static int SetCSS(int mode)       => 0;

        public static SetupData GetRealTimeState()                 => new SetupData();
        public static int SaveStateOnceOnly(string id)             => RS274NGC_OK;
        public static bool StateSaved()                            => false;

        public static int DoReverseSearch(string script, int code) => RS274NGC_OK;

        // Unit conversion stubs
        public static double UserUnitsToInches(double v)      => v;
        public static double UserUnitsToInchesX(double v)     => v;
        public static double UserUnitsToInchesOrDegA(double v)=> v;
        public static double UserUnitsToInchesOrDegB(double v)=> v;
        public static double UserUnitsToInchesOrDegC(double v)=> v;
        public static double InchesToUserUnits(double v)      => v;
        public static double InchesToUserUnitsX(double v)     => v;
        public static double InchesOrDegToUserUnitsA(double v)=> v;
        public static double InchesOrDegToUserUnitsB(double v)=> v;
        public static double InchesOrDegToUserUnitsC(double v)=> v;
        public static double ConvertAbsToUserUnitsX(double v) => v;
        public static double ConvertAbsToUserUnitsY(double v) => v;
        public static double ConvertAbsToUserUnitsZ(double v) => v;
        public static double ConvertAbsToUserUnitsA(double v) => v;
        public static double ConvertAbsToUserUnitsB(double v) => v;
        public static double ConvertAbsToUserUnitsC(double v) => v;
        public static double ConvertAbsToUserUnitsU(double v) => v;
        public static double ConvertAbsToUserUnitsV(double v) => v;

        /// <summary>
        /// Represents an M-code action record.
        /// </summary>
        public struct MCodeAction
        {
            public int Action;
            public double[] DParams;
            public string ParameterString;


            public MCodeAction(int action)
            {
                Action = action;
                DParams = new double[MAX_MCODE_DOUBLE_PARAMS];
                ParameterString = string.Empty;
                return;
            }
        }

        /// <summary>
        /// Static translator for legacy scripts.
        /// </summary>
        public static class Translator
        {
                public static string Translate(string input)
            {
                // TODO: wire in your legacy translation logic or stub:
                return input;
             }
        }

        /// <summary>
        /// Full C# port of the DynoMotion CGCodeInterpreter class.
        /// </summary>
        /// <remarks>
        /// Constructor with reference to the coordination/motion engine.
        /// </remarks>
        public class GCodeInterpreter(CCoordMotion coordMotion) : IDisposable
        {
            // --- Constants ---
            //private const int INTERP_TEXT_SIZE = 4096;

            // --- Core fields ---
            private readonly CCoordMotion CoordMotion = coordMotion;
            private bool m_Halt;
            private bool m_HaltNextLine;
            private int m_CurrentLine;
            private int? m_GCodeReads;
            private string? m_InFile;
            private int m_exitcode;
            private int m_InvokeExitcode;
            private Thread? m_InterpretThread;
            private Thread? m_InvokeThread;

            // --- File paths ---
            public string? ToolFile { get; private set; }
            public string? SetupFile { get; private set; }
            public string? GeoFile { get; private set; }
            public string? VarsFile { get; private set; }

            // --- Callbacks ---
            private GStatusCallback? _statusFn;
            private GCompleteCallback? _completeFn;
            //private GUserCallback? _userFn;
            //private GMUserCallback? _userFnMCode;
            //private GScreenScriptCallback? _screenScriptCallback;

            // --- Interpreter state ---
            private SetupData? _setup;
            // in GCodeInterpreter.cs, inside the GCodeInterpreter class
            public double CurrentFeedRate => _setup!.feed_rate;

            public string? ErrorOutput { get; private set; }
            //private bool? m_StateSaved;
            //private bool? _streaming;

            // --- M-code actions ---
            //public MCodeAction[]? McodeActions { get; }




            /// <summary>
            /// Starts asynchronous interpretation of the specified G-code file.
            /// </summary>
            public int Interpret(string fileName, int start, int end, int restart, GStatusCallback statusFn, GCompleteCallback completeFn)
            {
                CoordMotion.ClearAbort();
                CoordMotion.AxisDisabled = false;
                CoordMotion.RapidParamsDirty = true;

                m_InFile = fileName;
                m_CurrentLine = start;
                m_GCodeReads = 0;
                m_Halt = m_HaltNextLine = false;
                m_exitcode = m_InvokeExitcode = 0;
                _statusFn = statusFn;
                _completeFn = completeFn;

                m_InterpretThread = new Thread(DoExecuteShell!) { IsBackground = true };
                m_InterpretThread.Start(this);
                return m_InterpretThread.ManagedThreadId;
            }

            /// <summary>
            /// Publicly exposes launch of the interpreter thread.
            /// </summary>
            public int LaunchExecution()
            {
                if (m_InterpretThread == null || !m_InterpretThread.IsAlive)
                {
                    m_InterpretThread = new Thread(DoExecuteShell!) { IsBackground = true };
                    m_InterpretThread.Start(this);
                }
                return m_InterpretThread.ManagedThreadId;
            }

            /// <summary>
            /// Internal thread entry to run the interpreter.
            /// </summary>
            private static void DoExecuteShell(object param)
            {
                if (param is GCodeInterpreter interp)
                {
                    interp.m_exitcode = interp.DoExecute();
                    interp.DoExecuteComplete();
                }
            }

            /// <summary>
            /// Core interpreter loop: initializes RS274NGC, loads optional files, streams G-code lines,
            /// and invokes the engine per line, reporting status callbacks.
            /// </summary>
            public int DoExecute()
            {
                ErrorOutput = string.Empty;
                CoordMotion.DownloadInit();

                int status = 1;
                if (status != RS274NGC_OK) return ExitWithError(status);

                if (!string.IsNullOrEmpty(SetupFile) && (status = ReadSetup(SetupFile)) != RS274NGC_OK)
                    return ExitWithError(status);
                if (!string.IsNullOrEmpty(VarsFile) && (status = ReadVars(VarsFile)) != RS274NGC_OK)
                    return ExitWithError(status);
                if (!string.IsNullOrEmpty(GeoFile) && (status = ReadGeo(GeoFile)) != RS274NGC_OK)
                    return ExitWithError(status);
                if (!string.IsNullOrEmpty(ToolFile) && (status = ReadTool(ToolFile)) != RS274NGC_OK)
                    return ExitWithError(status);

                int programStatus = RS274NGC_OK;
                using (var reader = new StreamReader(m_InFile!))
                {
                    string line;
                    while ((programStatus == RS274NGC_OK) && reader.Peek() >= 0)
                    {
                        if (m_Halt) break;

                        line = reader.ReadLine()!;
                        m_GCodeReads++;
                        if (line == null) break;

                        programStatus = Execute(line);
                        if (programStatus != RS274NGC_OK)
                            return ExitWithError(programStatus);

                        _statusFn?.Invoke(m_CurrentLine++, GetLastMessage());
                    }
                    
                }

           // var tracker = new SetupTracker();
            //tracker.InsertState(_setup);   
            return programStatus;
            }

            /// <summary>
            /// Called after DoExecute to finalize downloads and invoke completion callback.
            /// </summary>
            private void DoExecuteComplete()
            {
                //CoordMotion.DownloadFinish();
                _completeFn?.Invoke(m_exitcode, m_CurrentLine, m_InvokeExitcode, ErrorOutput!);
            }

            /// <summary>
            /// Uniform exit helper to report errors.
            /// </summary>
            private int ExitWithError(int code)
            {
                _completeFn?.Invoke(code, m_CurrentLine, m_InvokeExitcode, ErrorOutput!);
                return code;
            }

            // --- Execution control ---
            public void Halt() => m_Halt = true;
            public void ClearHalt() => m_Halt = false;
            public bool GetHalt() => m_Halt;
            public bool GetHaltNextLine() => m_HaltNextLine;
            public int InitializeInterp() => Init(_setup);
            public void SetFeedRate(double feedRate) => RS274NGC.SetFeedRate(feedRate);
            public int SetCSS(int mode) => RS274NGC.SetCSS(mode);
            public SetupData GetRealTimeState() => RS274NGC.GetRealTimeState();

            // --- Parameter & state save ---
            public int SaveParameters() => RS274NGC.SaveParameters();
            public bool SaveParametersChanged() => RS274NGC.SaveParametersChanged();
            public int SaveStateOnceOnly() => RS274NGC.SaveStateOnceOnly();
            public bool StateSaved() => RS274NGC.StateSaved();

            // --- Reverse search ---
            public int DoReverseSearch(string script, int code) => RS274NGC.DoReverseSearch(script, code);

            // --- File & setup management ---
            public void SetToolFile(string f) => ToolFile = f;
            public void SetSetupFile(string f) => SetupFile = f;
            public void SetGeoFile(string f) => GeoFile = f;
            public void SetVarsFile(string f) => VarsFile = f;

            public int ReadToolFile() => RS274NGC.ReadTool(ToolFile!);
            public int ReadSetupFile() => RS274NGC.ReadSetup(SetupFile!);
            public int ReadGeoFile() => RS274NGC.ReadGeo(GeoFile!);
            public int ReadVarsFile() => RS274NGC.ReadVars(VarsFile!);

            // --- M-code action invocation ---
            public int InvokeAction(int action, bool wait = true) => RS274NGC.InvokeAction(action, wait);
            public int InvokeAction(MCodeAction action, bool wait = true)
                => RS274NGC.InvokeActionCustom(action.Action, wait, action.DParams, action.ParameterString);
            public int InvokeActionDirect(int action, bool wait, MCodeAction actionStruct)
                => RS274NGC.InvokeActionDirect(action, wait, actionStruct);

            // --- Fixture & origin controls ---
            public int ChangeFixtureNumber(int fixture)
                => RS274NGC.ExecuteChangeFixture(fixture);

            public int SetOrigin(int index, double x, double y, double z, double a, double b, double c)
                => RS274NGC.SetOrigin(index, x, y, z, a, b, c);
            public int GetOrigin(int index, out double x, out double y, out double z, out double a, out double b, out double c)
                => RS274NGC.GetOrigin(index, out x, out y, out z, out a, out b, out c);

            // --- Read-and-sync positions ---
            public int ReadAndSyncCurPositions(out double x, out double y, out double z,
                                               out double a, out double b, out double c,
                                               out double u, out double v)
                => RS274NGC.ReadAndSyncCurPositions(out x, out y, out z, out a, out b, out c, out u, out v);

            // --- Motion parameters ---

            // --- Unit conversions ---
            public double UserUnitsToInches(double v) => RS274NGC.UserUnitsToInches(v);
            public double UserUnitsToInchesX(double v) => RS274NGC.UserUnitsToInchesX(v);
            public double UserUnitsToInchesOrDegA(double v) => RS274NGC.UserUnitsToInchesOrDegA(v);
            public double UserUnitsToInchesOrDegB(double v) => RS274NGC.UserUnitsToInchesOrDegB(v);
            public double UserUnitsToInchesOrDegC(double v) => RS274NGC.UserUnitsToInchesOrDegC(v);

            public double InchesToUserUnits(double v) => RS274NGC.InchesToUserUnits(v);
            public double InchesToUserUnitsX(double v) => RS274NGC.InchesToUserUnitsX(v);
            public double InchesOrDegToUserUnitsA(double v) => RS274NGC.InchesOrDegToUserUnitsA(v);
            public double InchesOrDegToUserUnitsB(double v) => RS274NGC.InchesOrDegToUserUnitsB(v);
            public double InchesOrDegToUserUnitsC(double v) => RS274NGC.InchesOrDegToUserUnitsC(v);

            // --- Absolute conversions ---
            public double ConvertAbsToUserUnitsX(double x) => RS274NGC.ConvertAbsToUserUnitsX(x);
            public double ConvertAbsToUserUnitsY(double y) => RS274NGC.ConvertAbsToUserUnitsY(y);
            public double ConvertAbsToUserUnitsZ(double z) => RS274NGC.ConvertAbsToUserUnitsZ(z);
            public double ConvertAbsToUserUnitsA(double a) => RS274NGC.ConvertAbsToUserUnitsA(a);
            public double ConvertAbsToUserUnitsB(double b) => RS274NGC.ConvertAbsToUserUnitsB(b);
            public double ConvertAbsToUserUnitsC(double c) => RS274NGC.ConvertAbsToUserUnitsC(c);
            public double ConvertAbsToUserUnitsU(double u) => RS274NGC.ConvertAbsToUserUnitsU(u);
            public double ConvertAbsToUserUnitsV(double v) => RS274NGC.ConvertAbsToUserUnitsV(v);

            public void ConvertAbsoluteToInterpreterCoord(double x, double y, double z, double a, double b, double c,
                                                          out double gx, out double gy, out double gz,
                                                          out double ga, out double gb, out double gc)
                => RS274NGC.ConvertAbsoluteToInterpreter(x, y, z, a, b, c, out gx, out gy, out gz, out ga, out gb, out gc);
            public void ConvertAbsoluteToInterpreterCoord(double x, double y, double z,
                                                          double a, double b, double c, double u, double v,
                                                          out double gx, out double gy, out double gz,
                                                          out double ga, out double gb, out double gc,
                                                          out double gu, out double gv)
                => RS274NGC.ConvertAbsoluteToInterpreterFull(x, y, z, a, b, c, u, v,
                                                             out gx, out gy, out gz,
                                                             out ga, out gb, out gc,
                                                             out gu, out gv);
            public void ConvertAbsoluteToMachine(double x, double y, double z, double a, double b, double c,
                                                 out double mx, out double my, out double mz,
                                                 out double ma, out double mb, out double mc)
                => RS274NGC.ConvertAbsoluteToMachine(x, y, z, a, b, c, out mx, out my, out mz, out ma, out mb, out mc);
            public void ConvertAbsoluteToMachine(double x, double y, double z, double a, double b, double c,
                                                 double u, double v,
                                                 out double mx, out double my, out double mz,
                                                 out double ma, out double mb, out double mc,
                                                 out double mu, out double mv)
                => RS274NGC.ConvertAbsoluteToMachine(x, y, z, a, b, c, u, v,
                                                     out mx, out my, out mz,
                                                     out ma, out mb, out mc,
                                                     out mu, out mv);

            // --- Thread management ---
            public void Join(int timeoutMs = 100)
            {
                if (m_InterpretThread?.IsAlive == true) m_InterpretThread.Join(timeoutMs);
                if (m_InvokeThread?.IsAlive == true) m_InvokeThread.Join(timeoutMs);
            }

            /// <summary>
            /// Releases resources.
            /// </summary>
            public void Dispose()
            {
                Join();
                GC.SuppressFinalize(this);
            }
            /// <summary>
            /// Handle a feed‐rate segment coming from the trajectory planner.
            /// </summary>
            public static void OnFeedSegment(KEngine.SEGMENT seg)
            {
                // translate your SEGMENT fields into a Canon.STRAIGHT_FEED call
                Canon.STRAIGHT_FEED(seg.x, seg.y, seg.z, seg.a, seg.b, seg.c, seg.u, seg.v);
            }

            /// <summary>
            /// Handle a rapid‐traverse segment coming from the trajectory planner.
            /// </summary>
            public static void OnRapidSegment(KEngine.SEGMENT seg)
            {
                // similarly call the rapid traversal macro
                Canon.STRAIGHT_TRAVERSE(seg.x, seg.y, seg.z, seg.a, seg.b, seg.c, seg.u, seg.v, noCallback: true, seq: seg.sequence_number, id: seg.ID);
            }
        }
        



         // core interpreter entrypoints (P/Invoke or real C# wrappers go here)




    // parameter & state save
    public static int  SaveParameters()                        => throw new NotImplementedException();
    public static int  SaveStateOnceOnly()                     => throw new NotImplementedException();


    // reverse search


    // m-code invocations
    public static int InvokeAction(int action, bool wait = true)
                                                                => throw new NotImplementedException();
    public static int InvokeActionCustom(int action, bool wait, double[] dParams, string paramStr)
                                                                => throw new NotImplementedException();
    public static int InvokeActionDirect(int action, bool wait, MCodeAction actionStruct)
                                                                => throw new NotImplementedException();

    // fixture & origin
    public static int ExecuteChangeFixture(int fixture)         => throw new NotImplementedException();
    public static int SetOrigin(int idx, double x, double y, double z,
                                double a, double b, double c)
                                                                => throw new NotImplementedException();
    public static int GetOrigin(int idx,
                                out double x, out double y, out double z,
                                out double a, out double b, out double c)
                                                                => throw new NotImplementedException();

    // read & sync positions
    public static int ReadAndSyncCurPositions(
        out double x, out double y, out double z,
        out double a, out double b, out double c,
        out double u, out double v)
      => throw new NotImplementedException();



    public static void ConvertAbsoluteToInterpreter(
        double x, double y, double z, double a, double b, double c,
        out double gx, out double gy, out double gz,
        out double ga, out double gb, out double gc)
      => throw new NotImplementedException();

    public static void ConvertAbsoluteToInterpreterFull(
        double x, double y, double z, double a, double b, double c, double u, double v,
        out double gx, out double gy, out double gz,
        out double ga, out double gb, out double gc,
        out double gu, out double gv)
      => throw new NotImplementedException();

    public static void ConvertAbsoluteToMachine(
        double x, double y, double z, double a, double b, double c,
        out double mx, out double my, out double mz,
        out double ma, out double mb, out double mc)
      => throw new NotImplementedException();

    public static void ConvertAbsoluteToMachine(
        double x, double y, double z, double a, double b, double c, double u, double v,
        out double mx, out double my, out double mz,
        out double ma, out double mb, out double mc,
        out double mu, out double mv)
      => throw new NotImplementedException();
    }
}