using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Avalonia.Markup.Xaml.MarkupExtensions;
using AvaloniaEdit.Editing;

namespace KognaServer.Server.KinematicEngine
{
    public partial class RS274NGC
    {


        // --- Limits from rs274ngc.h --- :contentReference[oaicite:1]{index=1}
        private const int RS274NGC_TEXT_SIZE = 256;
        private const int RS274NGC_MAX_PARAMETERS = 5400;   // actual value from header
        private const int MAX_PARAM_CHANGES = 50;
        private const int RS274NGC_ACTIVE_G_CODES = 12;
        private const int RS274NGC_ACTIVE_M_CODES = 7;
        private const int RS274NGC_ACTIVE_SETTINGS = 3;
        private const int CANON_TOOL_MAX = 100;    // placeholder

        // --- “Required” parameters list for restore/save --- :contentReference[oaicite:2]{index=2}
        private static readonly int[] requiredParameters = new int[]
        {
            5161,5162,5163,
            5164,5165,5166,
            5181,5182,5183,
            5184,5185,5186,
            5211,5212,5213,
            5214,5215,5216,
            5220,
            5221,5222,5223,
            5224,5225,5226,
            5241,5242,5243,
            5244,5245,5246,
            5261,5262,5263,
            5264,5265,5266,
            5281,5282,5283,
            5284,5285,5286,
            5301,5302,5303,
            5304,5305,5306,
            5321,5322,5323,
            5324,5325,5326,
            5341,5342,5343,
            5344,5345,5346,
            5361,5362,5363,
            5364,5365,5366,
            5381,5382,5383,
            5384,5385,5386,
            RS274NGC_MAX_PARAMETERS
        };


        // --- Internal ring‐buffer for save_parameters_changed() ---
        private static int _nActualParametersToSave = 0;
        private static int[] _actualParametersToSave = new int[RS274NGC_MAX_PARAMETERS];
        private static double[] _actualParameterValues = new double[RS274NGC_MAX_PARAMETERS];

        // --- interfaces?

        public static SetupData _setup;




        /// <summary>
        /// Initialize interpreter. :contentReference[oaicite:4]{index=4}
        /// </summary>
        public static int Init()
        {
            _setup = new SetupData();
            // call into your hardware interface
            Canon.INIT_CANON();
            _setup.stack_index = 0;

            _setup.length_units = Canon.GET_EXTERNAL_LENGTH_UNIT_TYPE();
            Canon.USE_LENGTH_UNITS(_setup.length_units);

            var fn = Canon.GET_EXTERNAL_PARAMETER_FILE_NAME();
            if (string.IsNullOrEmpty(fn))
                fn = RS274NGC_PARAMETER_FILE_NAME_DEFAULT;
            var status = RestoreParameters(fn);
            if (status != RS274NGC_OK) return status;

            var pars = _setup.parameters;
            _setup.origin_index = (int)(pars[5220] + 0.0001);
            if (_setup.origin_index < 1 || _setup.origin_index > 9)
                return NCE_COORDINATE_SYSTEM_INDEX_PARAMETER_5220_OUT_OF_RANGE;

            int k = 5200 + _setup.origin_index * 20;
            Canon.SET_ORIGIN_OFFSETS(
                pars[k + 1] + pars[5211], pars[k + 2] + pars[5212], pars[k + 3] + pars[5213],
                pars[k + 4] + pars[5214], pars[k + 5] + pars[5215], pars[k + 6] + pars[5216],
                pars[k + 7] + pars[5217], pars[k + 8] + pars[5218]);

            Canon.SET_FEED_REFERENCE(CANON_FEED_REFERENCE.Workpiece);

            _setup.AA_axis_offset = pars[5214];
            _setup.AA_origin_offset = pars[k + 4];

            LoadToolTable();
            Reset();
            return RS274NGC_OK;
        }

        /// <summary>
        /// Execute one line or block (MDI or previously read). :contentReference[oaicite:5]{index=5}
        /// </summary>
        public static int Execute(string command = null)
        {
            int status = CHECK_INIT_ON_EXEC();
            if (status != RS274NGC_OK) return status;

            if (!string.IsNullOrEmpty(command))
                status = Read(command);

            // copy any parameter settings into parameters[]
            for (int i = 0; i < _setup.parameter_occurrence; i++)
                _setup.parameters[_setup.parameter_numbers[i]] =
                    _setup.parameter_values[i];

            if (_setup.line_length != 0)
            {
                status = ExecuteBlock(_setup.blocktext, _setup);
                WriteGCodes(_setup.block1, _setup);
                WriteMCodes(_setup.block1, _setup);
                WriteSettings(_setup);
                if (status != RS274NGC_OK &&
                    status != RS274NGC_EXECUTE_FINISH &&
                    status != RS274NGC_EXIT)
                    return status;
            }
            else
            {
                status = RS274NGC_OK;
            }
            return status;
        }

        /// <summary>
        /// Exit interpreter, save parameters. :contentReference[oaicite:6]{index=6}
        /// </summary>
        public static int Exit()
        {
            var fn = Canon.GetCanonParameterFileName();
            if (string.IsNullOrEmpty(fn))
                fn = RS274NGC_PARAMETER_FILE_NAME_DEFAULT;
            SaveParameters(fn);
            Reset();
            return RS274NGC_OK;
        }

        /// <summary>
        /// Reset line‐interpretation state. :contentReference[oaicite:7]{index=7}
        /// </summary>
        public static int Reset()
        {
            _setup.linetext[0] = '\0';
            _setup.blocktext[0] = '\0';
            _setup.line_length = 0;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Restore parameters from disk, enforcing _requiredParameters. :contentReference[oaicite:8]{index=8}
        /// </summary>
        public static int RestoreParameters(string filename)
        {
            double[] pars = _setup.parameters;
            Array.Clear(pars, 0, pars.Length);

            if (!File.Exists(filename))
                return NCE_UNABLE_TO_OPEN_FILE;

            using var reader = new StreamReader(filename);
            int requiredIdx = 0;
            int nextRequired = _requiredParameters[requiredIdx++];

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                if (!int.TryParse(parts[0], out var variable) ||
                    !double.TryParse(parts[1], out var value))
                    continue;

                if (variable <= 0 || variable >= RS274NGC_MAX_PARAMETERS)
                    return NCE_PARAMETER_NUMBER_OUT_OF_RANGE;

                // fast‐forward to the variable
                while (variable > nextRequired)
                {
                    if (nextRequired == _requiredParameters[^1])
                        return NCE_REQUIRED_PARAMETER_MISSING;
                    nextRequired = _requiredParameters[requiredIdx++];
                }
                if (variable < nextRequired)
                    return NCE_PARAMETER_FILE_OUT_OF_ORDER;

                pars[variable] = value;
                _actualParametersToSave[_nActualParametersToSave] = variable;
                _actualParameterValues[_nActualParametersToSave++] = value;

                nextRequired = (requiredIdx < _requiredParameters.Length)
                    ? _requiredParameters[requiredIdx++]
                    : RS274NGC_MAX_PARAMETERS;
            }

            if (_requiredParameters[^1] != RS274NGC_MAX_PARAMETERS)
                return NCE_REQUIRED_PARAMETER_MISSING;

            // force 5220 ≥ 1
            if (pars[5220] < 1.0) pars[5220] = 1.0;

            return RS274NGC_OK;
        }

        /// <summary>
        /// Save parameters back to disk (renaming original to .bak). :contentReference[oaicite:9]{index=9}
        /// </summary>
        public static int SaveParameters(string filename)
        {
            string backup = filename + ".bak";
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(filename, backup);

            // reopen backup for reading
            using var reader = new StreamReader(backup);
            using var writer = new StreamWriter(filename, false);

            int requiredIdx = 0;
            int nextRequired = _requiredParameters[requiredIdx++];

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null) break;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                if (!int.TryParse(parts[0], out var variable))
                    continue;

                if (variable <= 0 || variable >= RS274NGC_MAX_PARAMETERS)
                    return NCE_PARAMETER_NUMBER_OUT_OF_RANGE;

                while (variable > nextRequired)
                {
                    if (nextRequired == _requiredParameters[^1])
                        break;
                    nextRequired = _requiredParameters[requiredIdx++];
                }

                writer.WriteLine($"{variable}\t{_setup.parameters[variable]}");
            }

            // ensure any missing required parameters get written
            foreach (var req in _requiredParameters)
            {
                if (!_actualParametersToSave[.._nActualParametersToSave].Contains(req))
                    writer.WriteLine($"{req}\t{_setup.parameters[req]}");
            }

            return RS274NGC_OK;
        }

        /// <summary>
        /// Has any parameter changed since last restore? :contentReference[oaicite:10]{index=10}
        /// </summary>
        public static bool SaveParametersChanged()
            => _nActualParametersToSave > 0;

        /// <summary>
        /// Synchronize internal state with Canon controller. :contentReference[oaicite:11]{index=11}
        /// </summary>
        private static int Synch()
        {
            _setup.control_mode = Canon.GET_EXTERNAL_MOTION_CONTROL_MODE();
            _setup.spindle_mode = Canon.GET_EXTERNAL_SPINDLE_MODE();
            _setup.AA_current = Canon.GET_EXTERNAL_POSITION_A();
            _setup.BB_current = Canon.GET_EXTERNAL_POSITION_B();
            _setup.CC_current = Canon.GET_EXTERNAL_POSITION_C();
            _setup.UU_current = Canon.GET_EXTERNAL_POSITION_U();
            _setup.VV_current = Canon.GET_EXTERNAL_POSITION_V();
            _setup.feed_rate = Canon.GetExternalFeedRate();
            _setup.flood = Canon.GetExternalFlood() != 0;
            _setup.length_units = Canon.GET_EXTERNAL_LENGTH_UNIT_TYPE();
            _setup.mist = (Canon.GET_EXTERNAL_MIST() != 0 )? 1 : 0;
            _setup.plane = Canon.GET_EXTERNAL_PLANE() ;
            _setup.selected_tool_slot = Canon.GET_EXTERNAL_TOOL_SLOT();
            _setup.speed = Canon.GET_EXTERNAL_SPEED();
            _setup.spindle_turning = Canon.GET_EXTERNAL_SPINDLE();
            _setup.tool_max = Canon.GET_EXTERNAL_TOOL_MAX();
            _setup.traverse_rate = Canon.GET_EXTERNAL_TOOL_SLOT();
            _setup.arc_radius_tol = Canon.GET_DEFAULT_ARC_TOLERANCE();

            LoadToolTable();
            return RS274NGC_OK;
        }

        /// <summary>
        /// Load the entire tool table from external world. :contentReference[oaicite:12]{index=12}
        /// </summary>
        public static int LoadToolTable()
        {
            if (_setup.tool_max > CANON_TOOL_MAX)
                return NCE_TOOL_MAX_TOO_LARGE;

            for (int n = 0; n <= _setup.tool_max; n++)
                _setup.tool_table[n] = Canon.GET_EXTERNAL_TOOL_TABLE(n);

            for (int n = _setup.tool_max + 1; n <= CANON_TOOL_MAX; n++)
                _setup.tool_table[n] = new CANON_TOOL_TABLE();

            return RS274NGC_OK;
        }

        /// <summary>
        /// Open an NC‐code file (with '%' handling). :contentReference[oaicite:13]{index=13}
        /// </summary>
        public static int Open(string filename)
        {
            if (_setup.file_pointer != null) return NCE_A_FILE_IS_ALREADY_OPEN;
            if (filename.Length >= RS274NGC_TEXT_SIZE) return NCE_COMMAND_TOO_LONG;

            try
            {
                _setup.file_pointer = new StreamReader(filename);
            }
            catch
            {
                return NCE_UNABLE_TO_OPEN_FILE;
            }

            int result = SkipPercent();
            if (result != RS274NGC_OK) return result;

            _setup.filename = filename;
            Reset();
            return RS274NGC_OK;
        }

        private static int SkipPercent()
        {
            string line;
            bool seenFirstPercent = false;

            while ((line = _setup.file_pointer.ReadLine()) != null)
            {
                if (line.Trim() == "%")
                {
                    if (!seenFirstPercent)
                    {
                        seenFirstPercent = true;
                        _setup.sequence_number = 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (!seenFirstPercent)
                _setup.file_pointer.BaseStream.Seek(0, SeekOrigin.Begin);

            return RS274NGC_OK;
        }

        /// <summary>
        /// Read next NC line or MDI text. :contentReference[oaicite:14]{index=14}
        /// </summary>
        public static int Read(string mdi = null)
        {
            if (mdi == null && _setup.file_pointer == null)
                return NCE_FILE_NOT_OPEN;

            string text = mdi ?? _setup.file_pointer.ReadLine();
            if (text == null) return RS274NGC_ENDFILE;

            _setup.linetext = text.ToCharArray();
            _setup.line_length = _setup.linetext.Length;
            if (_setup.line_length > 0)
                ParseLine(_setup.linetext, out _setup.blocktext);
            return RS274NGC_OK;
        }

        // …and similarly port the “getter” APIs:

        public static void ActiveGCodes(int[] codes)
        {
            Array.Copy(_setup.active_g_codes, codes, RS274NGC_ACTIVE_G_CODES);
        }

        public static void ActiveMCodes(int[] codes)
        {
            Array.Copy(_setup.active_m_codes, codes, RS274NGC_ACTIVE_M_CODES);
        }

        public static void ActiveSettings(double[] settings)
        {
            Array.Copy(_setup.active_settings, settings, RS274NGC_ACTIVE_SETTINGS);
        }

        public static void ErrorText(int errorCode, StringBuilder errorText, int maxSize)
        {
            if (errorCode >= 0 && errorCode < Rs274NgcErrors.Messages.Count)
            {
                var msg = Rs274NgcErrors.Get(errorCode);
                if (msg.Length < maxSize)
                {
                    errorText.Clear();
                    errorText.Append(msg);
                }
            }
            else
            {
                errorText.Clear();
            }
        }

        public static void FileName(StringBuilder fileName, int maxSize)
        {
            var name = _setup.filename;
            if (name.Length < maxSize)
            {
                fileName.Clear();
                fileName.Append(name);
            }
            else
            {
                fileName.Clear();
            }
        }
        public static int LineLength() => _setup.line_length;

        public static void LineText(StringBuilder lineText, int maxSize)
        {
            var txt = new string(_setup.linetext).TrimEnd('\0');
            if (txt.Length >= maxSize) txt = txt.Substring(0, maxSize - 1);
            lineText.Clear();
            lineText.Append(txt);
        }

        public static int SequenceNumber() => _setup.sequence_number;

        public static void StackName(int index, StringBuilder functionName, int maxSize)
        {
            string name = string.Empty;
            if (_setup.stack != null && index >= 0 && index < _setup.stack.Length)
                name = _setup.stack[index] ?? string.Empty;
            if (name.Length < maxSize)
            {
                functionName.Clear();
                functionName.Append(name);
            }
            else
            {
                functionName.Clear();
            }
        }


        public static int IniLoad(string filename)
        {
            // No C++ body was provided; stubbed for the moment. :contentReference[oaicite:15]{index=15}
            throw new NotImplementedException();
        }

        // Convenience inlines from rs274ngc.h
        public static int Line() => SequenceNumber();
        public static string Command()
        {
            var sb = new StringBuilder(100);
            LineText(sb, 100);
            return sb.ToString();
        }
        public static string File()
        {
            var sb = new StringBuilder(100);
            FileName(sb, 100);
            return sb.ToString();
        }

        /// <summary>
        /// Mirrors the C struct block_struct. :contentReference[oaicite:1]{index=1}
        /// </summary>
        private class Block
        {
            public bool a_flag; public double a_number;
            public bool b_flag; public double b_number;
            public bool c_flag; public double c_number;
            public bool u_flag; public double u_number;
            public bool v_flag; public double v_number;
            public char[] comment = new char[256];
            public int d_number; public bool d_flag;
            public double f_number;
            public int[] g_modes = new int[15];
            public int h_number;
            public bool i_flag; public double i_number;
            public bool j_flag; public double j_number;
            public bool k_flag; public double k_number;
            public bool l_flag; public int l_number;
            public int line_number;
            public int motion_to_be;
            public int m_count; public int[] m_modes = new int[11];
            public bool p_flag; public double p_number;
            public bool q_flag; public double q_number;
            public bool r_flag; public double r_number;
            public double s_number;
            public int t_number;
            public bool x_flag; public double x_number;
            public bool y_flag; public double y_number;
            public bool z_flag; public double z_number;
        }

        /// <summary>
        /// Reset every field in a Block to its “not present” sentinel. :contentReference[oaicite:2]{index=2}
        /// </summary>
        private static int InitBlock(Block b)
        {
            b.a_flag = b.b_flag = b.c_flag = b.u_flag = b.v_flag = false;
            b.comment[0] = '\0';
            b.d_number = -1; b.d_flag = false;
            b.f_number = -1.0;
            for (int i = 0; i < 15; i++) b.g_modes[i] = -1;
            b.h_number = -1;
            b.i_flag = b.j_flag = b.k_flag = false;
            b.l_flag = false; b.l_number = -1;
            b.line_number = -1;
            b.motion_to_be = -1;
            b.m_count = 0;
            for (int i = 0; i < 11; i++) b.m_modes[i] = -1;
            b.p_flag = b.q_flag = b.r_flag = false;
            b.p_number = b.q_number = b.r_number = -1.0;
            b.s_number = -1.0;
            b.t_number = -1;
            b.x_flag = b.y_flag = b.z_flag = false;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Top‐level line parser. Calls init_block, read_items, enhance_block, check_items. :contentReference[oaicite:3]{index=3}</summary>
        private static int ParseLine(string line, out Block block)
        {
            block = new Block();
            int status = InitBlock(block);
            if (status != RS274NGC_OK) return status;

            // Read every word (G, M, X, Y, Z, etc.)
            // This loops over the line buffer with read_* functions:
            //   read_real_value, read_g, read_m, read_parameter_setting, …
            // e.g. CHK(read_items(block, line, ref counter, _setup.parameters));
            status = ReadItems(block, line, _setup.parameters);
            if (status != RS274NGC_OK) return status;

            // Apply canned‐cycle / motion‐to‐be logic
            status = EnhanceBlock(block, _setup);
            if (status != RS274NGC_OK) return status;

            // Validate word‐combinations (I‐J‐K only with arcs, no duplicate axes, etc.)
            status = CheckItems(block, _setup);
            return status;
        }

        /// <summary>
        /// Execute one block of parsed instructions in the correct order:
        /// comment → feed-mode → feed-rate → S → T → M → G → stop. :contentReference[oaicite:4]{index=4}</summary>
        private static int ExecuteBlock(Block block, SetupData s)
        {
            int status;
            if (block.comment[0] != '\0')
                CHP(ConvertComment(new string(block.comment)));

            if (block.g_modes[5] != -1)
                CHP(ConvertFeedMode(block.g_modes[5], s));

            if (block.f_number > -1.0 && s.feed_mode != DistanceMode.INVERSE_TIME)
                CHP(ConvertFeedRate(block, s));

            if (block.g_modes[14] != -1)
            {
                // G96/G97
                CHP(ConvertSpindleMode(block.g_modes[14], s));
            }

            if (block.s_number > -1.0)
                CHP(ConvertSpeed(block, s));

            if (block.t_number != -1)
                CHP(ConvertToolSelect(block, s));

            CHP(ConvertM(block, s));
            CHP(ConvertG(block, s));

            if (block.m_modes[4] != -1)
            {
                status = ConvertStop(block, s);
                if (status == RS274NGC_EXIT) return RS274NGC_EXIT;
                else if (status != RS274NGC_OK) ERM(status);
            }

            return s.probe_flag == ON ? RS274NGC_EXECUTE_FINISH : RS274NGC_OK;
        }

        /// <summary>
        /// Write out the active G-codes into the setup snapshot. :contentReference[oaicite:5]{index=5}</summary>
        private static int WriteGCodes(Block block, SetupData s)
        {
            var g = s.active_g_codes;
            g[0] = s.sequence_number;
            g[1] = s.motion_mode;
            g[2] = block == null ? -1 : block.g_modes[0];
            g[3] = s.plane == CanonPlane.XY ? G_17 : s.plane == CanonPlane.XZ ? G_18 : G_19;
            g[4] = s.cutter_comp_side == CutterComp.Left ? G_41 : s.cutter_comp_side == CutterComp.Right ? G_42 : G_40;
            g[5] = s.length_units == Units.Inches ? G_20 : G_21;
            g[6] = s.distance_mode == DistanceMode.ABSOLUTE ? G_90 : G_91;
            g[7] = s.feed_mode == FeedMode.INVERSE_TIME ? G_93 :
                    s.feed_mode == FeedMode.UNITS_PER_MINUTE ? G_94 : G_95;
            g[8] = s.origin_index < 7 ? 530 + 10 * s.origin_index : 584 + s.origin_index;
            g[9] = s.tool_length_offset == 0 && s.tool_xoffset == 0 && s.tool_yoffset == 0 ? G_49 : G_43;
            g[10] = s.retract_mode == RetractMode.OLD_Z ? G_98 : G_99;
            g[11] = s.control_mode == CanonControl.CONTINUOUS ? G_64 : G_61;
            g[12] = s.spindle_mode == SpindleMode.NORMAL ? G_97 : G_96;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Write out the active M-codes into the setup snapshot. :contentReference[oaicite:6]{index=6}</summary>
        private static int WriteMCodes(Block block, SetupData s)
        {
            var m = s.active_m_codes;
            m[0] = s.sequence_number;
            m[1] = block == null ? -1 : block.m_modes[4];
            m[2] = s.spindle_turning == SpindleState.STOPPED ? 5 :
                   s.spindle_turning == SpindleState.CW ? 3 : 4;
            m[3] = block == null ? -1 : block.m_modes[6];
            m[4] = s.mist ? 7 :
                   s.flood ? -1 : 9;
            m[5] = s.flood ? 8 : -1;
            m[6] = s.feed_override ? 48 : 49;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Write out feed, speed, and sequence into active_settings. :contentReference[oaicite:7]{index=7}</summary>
        private static int WriteSettings(SetupData s)
        {
            var a = s.active_settings;
            a[0] = s.sequence_number;
            a[1] = s.feed_rate;
            a[2] = s.speed;
            return RS274NGC_OK;
        }


        private static int ReadItems(Block block, string line, double[] parameters)
        {
            int length = line.Length;
            int counter = 0;

            // Skip leading "/" if block‐delete is on
            if (counter < length && line[counter] == '/')
            {
                counter++;
                if (_setup.block_delete)
                    length = 1;
            }

            // Optional N (or old "O") line number
            if (counter < length && (line[counter] == 'n' || line[counter] == 'o'))
            {
                int status = ReadLineNumber(line, ref counter, block);
                if (status != RS274NGC_OK) return status;
            }

            // Read remaining items
            while (counter < length)
            {
                int status = ReadOneItem(line, ref counter, block, parameters);
                if (status != RS274NGC_OK) return status;
            }

            return RS274NGC_OK;
        }
        private static int ReadOneItem(string line, ref int counter, Block block, double[] parameters)
        {
            char c = line[counter];
            int status;

            switch (c)
            {
                case 'x': status = ReadX(line, ref counter, block, parameters); break;
                case 'y': status = ReadY(line, ref counter, block, parameters); break;
                case 'z': status = ReadZ(line, ref counter, block, parameters); break;
                case 'a': status = ReadA(line, ref counter, block, parameters); break;
                case 'b': status = ReadB(line, ref counter, block, parameters); break;
                case 'c': status = ReadC(line, ref counter, block, parameters); break;
                case 'u': status = ReadU(line, ref counter, block, parameters); break;
                case 'v': status = ReadV(line, ref counter, block, parameters); break;
                case 'd': status = ReadD(line, ref counter, block, parameters); break;
                case 'f': status = ReadF(line, ref counter, block, parameters); break;
                case 'h': status = ReadH(line, ref counter, block, parameters); break;
                case 'i': status = ReadI(line, ref counter, block, parameters); break;
                case 'j': status = ReadJ(line, ref counter, block, parameters); break;
                case 'k': status = ReadK(line, ref counter, block, parameters); break;
                case 'l': status = ReadL(line, ref counter, block, parameters); break;
                case 'm': status = ReadM(line, ref counter, block, parameters); break;
                case 'p': status = ReadP(line, ref counter, block, parameters); break;
                case 'q': status = ReadQ(line, ref counter, block, parameters); break;
                case 'r': status = ReadR(line, ref counter, block, parameters); break;
                case 's': status = ReadS(line, ref counter, block, parameters); break;
                case 't': status = ReadT(line, ref counter, block, parameters); break;
                case '(': status = ReadComment(line, ref counter, block, parameters); break;
                case '#': status = ReadParameter(line, ref counter, out _, parameters); break;
                case 'g': status = ReadG(line, ref counter, block, parameters); break;
                default:
                    return NCE_BAD_CHARACTER_USED;
            }

            return status;
        }
        /// <summary>
        /// Read line number “N”/“O”: unsigned int → block.line_number. :contentReference[oaicite:2]{index=2}</summary>
        private static int ReadLineNumber(string line, ref int counter, Block block)
        {
            if (line[counter] != 'n' && line[counter] != 'o')
                return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;

            int value;
            int status = ReadIntegerUnsigned(line, ref counter, out value);
            if (status != RS274NGC_OK) return status;

            block.line_number = value;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Unsigned integer: one or more digits (no sign). :contentReference[oaicite:3]{index=3}</summary>
        private static int ReadIntegerUnsigned(string line, ref int counter, out int result)
        {
            int start = counter;
            while (counter < line.Length && char.IsDigit(line[counter]))
                counter++;
            if (counter == start)
            {
                result = 0;
                return NCE_BAD_FORMAT_UNSIGNED_INTEGER;
            }
            if (!int.TryParse(line.Substring(start, counter - start), out result))
                return NCE_SSCANF_FAILED;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Integer (via real + floor/ceil check). :contentReference[oaicite:4]{index=4}</summary>
        private static int ReadIntegerValue(string line, ref int counter, out int result, double[] parameters)
        {
            double realVal;
            int status = ReadRealValue(line, ref counter, out realVal, parameters);
            if (status != RS274NGC_OK) { result = 0; return status; }

            result = (int)Math.Floor(realVal);
            double frac = realVal - result;
            if (frac > 0.9999) result = (int)Math.Ceiling(realVal);
            else if (frac > 0.0001) return NCE_NON_INTEGER_VALUE_FOR_INTEGER;

            return RS274NGC_OK;
        }

        /// <summary>
        /// Read a real (number, [expr], parameter, or unary). :contentReference[oaicite:5]{index=5}</summary>
        private static int ReadRealValue(string line, ref int counter, out double result, double[] parameters)
        {
            if (counter >= line.Length) { result = 0; return NCE_NO_CHARACTERS_FOUND_IN_READING_REAL_VALUE; }
            char c = line[counter];
            int status;
            if (c == '[')
                status = ReadRealExpression(line, ref counter, out result, parameters);
            else if (c == '#')
                status = ReadParameter(line, ref counter, out result, parameters);
            else if ((c >= 'a' && c <= 'z') ||
                     (c == '-' && (counter + 1 < line.Length &&
                         !char.IsDigit(line[counter + 1]) && line[counter + 1] != '.')))
                status = ReadUnary(line, ref counter, out result, parameters);
            else
                status = ReadRealNumber(line, ref counter, out result);

            return status;
        }

        /// <summary>
        /// Number with optional sign & decimal. :contentReference[oaicite:6]{index=6}</summary>
        private static int ReadRealNumber(string line, ref int counter, out double result)
        {
            int n = counter;
            bool seenDigit = false, seenDot = false;

            // Leading +/- or first digit/dot
            if (line[n] == '+') { n++; counter++; }
            else if (line[n] == '-') { n++; }

            while (n < line.Length)
            {
                char c = line[n];
                if (char.IsDigit(c)) seenDigit = true;
                else if (c == '.' && !seenDot) { seenDot = true; }
                else break;
                n++;
            }
            if (!seenDigit) { result = 0; return NCE_NO_DIGITS_FOUND_WHERE_REAL_NUMBER_SHOULD_BE; }

            string token = line.Substring(counter, n - counter);
            if (!double.TryParse(token, out result)) return NCE_SSCANF_FAILED;
            counter = n;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Fully general “[ … ]” expression with stack. :contentReference[oaicite:7]{index=7}</summary>
        private static int ReadRealExpression(string line, ref int counter, out double result, double[] parameters)
        {
            if (line[counter] != '[') { result = 0; return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED; }
            counter++;

            const int MAX_STACK = 5;
            double[] values = new double[MAX_STACK];
            int[] ops = new int[MAX_STACK];
            int si = 0;

            // Read first value and operation
            int status = ReadRealValue(line, ref counter, out values[0], parameters);
            if (status != RS274NGC_OK) { result = 0; return status; }
            status = ReadOperation(line, ref counter, out ops[0]);
            if (status != RS274NGC_OK) { result = 0; return status; }

            // Process until RIGHT_BRACKET on ops[0]
            while (ops[0] != RIGHT_BRACKET)
            {
                status = ReadRealValue(line, ref counter, out values[++si], parameters);
                if (status != RS274NGC_OK) { result = 0; return status; }
                status = ReadOperation(line, ref counter, out ops[si]);
                if (status != RS274NGC_OK) { result = 0; return status; }

                while (si > 0 && Precedence(ops[si]) <= Precedence(ops[si - 1]))
                {
                    status = ExecuteBinary(ref values[si - 1], ops[si - 1], ref values[si]);
                    if (status != RS274NGC_OK) { result = 0; return status; }
                    ops[si - 1] = ops[si];
                    si--;
                }
            }
            result = values[0];
            return RS274NGC_OK;
        }

        /// <summary>
        /// Binary op: + - * / ** and, or, xor, mod, logicals, etc. :contentReference[oaicite:8]{index=8}</summary>
        private static int ReadOperation(string line, ref int counter, out int op)
        {
            char c = line[counter++];
            switch (c)
            {
                case '+': op = PLUS; break;
                case '-': op = MINUS; break;
                case '/': op = DIVIDED_BY; break;
                case '*':
                    if (counter < line.Length && line[counter] == '*') { op = POWER; counter++; }
                    else op = TIMES;
                    break;
                case ']': op = RIGHT_BRACKET; break;
                case 'a':
                    if (MatchAhead(line, counter, "nd")) { op = AND2; counter += 2; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_A;
                    break;
                case 'm':
                    if (MatchAhead(line, counter, "od")) { op = MODULO; counter += 2; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_M;
                    break;
                case 'o':
                    if (MatchAhead(line, counter, "r")) { op = NON_EXCLUSIVE_OR; counter++; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_O;
                    break;
                case 'x':
                    if (MatchAhead(line, counter, "or")) { op = EXCLUSIVE_OR; counter += 2; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_X;
                    break;
                case '<':
                    if (MatchAhead(line, counter, "=")) { op = LOGICAL_LE; counter++; }
                    else if (MatchAhead(line, counter, ">")) { op = LOGICAL_NE; counter++; }
                    else op = LOGICAL_LT;
                    break;
                case '>':
                    if (MatchAhead(line, counter, "=")) { op = LOGICAL_GE; counter++; }
                    else op = LOGICAL_GT;
                    break;
                case '=': op = LOGICAL_EQ; break;
                default: return NCE_UNKNOWN_OPERATION;
            }
            return RS274NGC_OK;
        }

        /// <summary>
        /// Match literal the rest of a token. </summary>
        private static bool MatchAhead(string s, int idx, string tok)
        {
            return idx + tok.Length <= s.Length && s.Substring(idx, tok.Length) == tok;
        }

        /// <summary>
        /// Unary op names: abs, acos, asin, atan, cos, exp, etc. :contentReference[oaicite:9]{index=9}</summary>
        private static int ReadUnary(string line, ref int counter, out double result, double[] parameters)
        {
            int operation;
            int status = ReadOperationUnary(line, ref counter, out operation);
            if (status != RS274NGC_OK) { result = 0; return status; }

            if (operation == UNEGATIVE)
            {
                status = ReadRealValue(line, ref counter, out result, parameters);
                if (status != RS274NGC_OK) return status;
            }
            else
            {
                if (counter >= line.Length || line[counter] != '[') { result = 0; return NCE_LEFT_BRACKET_MISSING_AFTER_UNARY_OPERATION_NAME; }
                status = ReadRealExpression(line, ref counter, out result, parameters);
                if (status != RS274NGC_OK) return status;
            }

            if (operation == ATAN)
                return ReadAtan(line, ref counter, ref result, parameters);
            else
                return ExecuteUnary(ref result, operation);
        }

        /// <summary>
        /// Atan(…) has two args: value/[expr]. :contentReference[oaicite:10]{index=10}</summary>
        private static int ReadAtan(string line, ref int counter, ref double val, double[] parameters)
        {
            if (counter >= line.Length || line[counter] != '/') return NCE_SLASH_MISSING_AFTER_FIRST_ATAN_ARGUMENT;
            counter++;
            if (counter >= line.Length || line[counter] != '[') return NCE_LEFT_BRACKET_MISSING_AFTER_SLASH_WITH_ATAN;
            int status = ReadRealExpression(line, ref counter, out double arg2, parameters);
            if (status != RS274NGC_OK) return status;

            // C++: atan2(y, x) but their args are reversed
            val = Math.Atan2(val, arg2) * (180.0 / Math.PI);
            return RS274NGC_OK;
        }

        // --- Now the coordinate readers, all follow the same pattern ---

        private static int ReadX(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'x', v => { b.x_flag = true; b.x_number = v; });
        private static int ReadY(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'y', v => { b.y_flag = true; b.y_number = v; });
        private static int ReadZ(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'z', v => { b.z_flag = true; b.z_number = v; });
        private static int ReadA(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'a', v => { b.a_flag = true; b.a_number = v; });
        private static int ReadB(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'b', v => { b.b_flag = true; b.b_number = v; });
        private static int ReadC(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'c', v => { b.c_flag = true; b.c_number = v; });
        private static int ReadU(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'u', v => { b.u_flag = true; b.u_number = v; });
        private static int ReadV(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'v', v => { b.v_flag = true; b.v_number = v; });
        private static int ReadF(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'f', v => b.f_number = v, mustBePositive: true);
        private static int ReadS(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 's', v => b.s_number = v, mustBePositive: true);
        private static int ReadP(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'p', v => { b.p_flag = true; b.p_number = v; });
        private static int ReadQ(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'q', v => { b.q_flag = true; b.q_number = v; });
        private static int ReadR(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'r', v => { b.r_flag = true; b.r_number = v; });
        private static int ReadT(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 't') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.t_number != -1) return NCE_MULTIPLE_T_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int t, p);
            if (status != RS274NGC_OK) return status;
            b.t_number = t;
            return RS274NGC_OK;
        }
        private static int ReadD(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'd') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.d_flag) return NCE_MULTIPLE_D_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int d, p);
            if (status != RS274NGC_OK) return status;
            if (d < 0) return NCE_NEGATIVE_D_WORD_TOOL_RADIUS_INDEX_USED;
            if (d > 99999 && b.g_modes[14] != G_96) return NCE_TOOL_RADIUS_INDEX_TOO_BIG;
            b.d_number = d; b.d_flag = true;
            return RS274NGC_OK;
        }
        private static int ReadH(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'h') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.h_number > -1) return NCE_MULTIPLE_H_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int h, p);
            if (status != RS274NGC_OK) return status;
            if (h < 0) return NCE_NEGATIVE_H_WORD_TOOL_LENGTH_OFFSET_INDEX_USED;
            if (h > 99999) return NCE_TOOL_LENGTH_OFFSET_INDEX_TOO_BIG;
            b.h_number = h;
            return RS274NGC_OK;
        }
        private static int ReadL(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'l') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.l_flag) return NCE_MULTIPLE_L_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int l, p);
            if (status != RS274NGC_OK) return status;
            if (l < 0) return NCE_NEGATIVE_L_WORD_USED;
            b.l_flag = true; b.l_number = l;
            return RS274NGC_OK;
        }
        private static int ReadM(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'm') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            int status = ReadIntegerValue(line, ref counter, out int mCode, p);
            if (status != RS274NGC_OK) return status;
            if (mCode < 0) return NCE_NEGATIVE_M_CODE_USED;
            if (mCode > 119) return NCE_M_CODE_GREATER_THAN_119;
            int modal = _ems[mCode];
            if (modal == -1) return NCE_UNKNOWN_M_CODE_USED;
            if (b.m_modes[modal] != -1) return NCE_TWO_M_CODES_USED_FROM_SAME_MODAL_GROUP;
            b.m_modes[modal] = mCode;
            b.m_count++;
            return RS274NGC_OK;
        }

        // Helper for all single-letter real-value readers
        private static int ReadAxis(string line, ref int counter, char letter, Action<double> assign, bool mustBePositive = false)
        {
            if (line[counter] != letter) return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            double val;
            int status = ReadRealValue(line, ref counter, out val, _setup.parameters);
            if (status != RS274NGC_OK) return status;
            if (mustBePositive && val < 0.0) return letter == 'f'
                ? NCE_NEGATIVE_F_WORD_USED
                : NCE_NEGATIVE_SPINDLE_SPEED_USED;
            assign(val);
            return RS274NGC_OK;
        }

        private static int ConvertComment(string comment)
        {
            int status = 0;
            int i = comment.IndexOf('(');
            int n = comment.IndexOf(')');
            while (i >= 0 && n > i)
            {
                string inner = comment.Substring(i + 1, n - i - 1);
                status = ConvertComment2(inner);
                if (status != 0) return status;
                i = comment.IndexOf('(', n + 1);
                n = comment.IndexOf(')', n + 1);
            }
            return status;
        }

        private static int ConvertComment2(string comment)
        {
            int m = 0;
            // skip leading space/tab
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length)
            {
                Comment(comment);
                return 0;
            }
            char c = comment[m];
            // must start MSG,
            if (char.ToUpperInvariant(c) != 'M')
            {
                Comment(comment);
                return 0;
            }
            // skip 'M'
            m++;
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length || char.ToUpperInvariant(comment[m]) != 'S')
            {
                Comment(comment);
                return 0;
            }
            // skip 'S'
            m++;
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length || char.ToUpperInvariant(comment[m]) != 'G')
            {
                Comment(comment);
                return 0;
            }
            // skip 'G'
            m++;
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length || comment[m] != ',')
            {
                Comment(comment);
                return 0;
            }
            // MSG, so print after comma
            Message(comment.Substring(m + 1));
            return 0;
        }

        // === feed mode (G93/94/95) ===

        private static int ConvertFeedMode(int gCode, SetupData s)
        {
            switch (gCode)
            {
                case G93:
                    Comment("interpreter: feed mode set to inverse time");
                    s.FeedMode = FeedMode.InverseTime;
                    break;
                case G94:
                    Comment("interpreter: feed mode set to units per minute");
                    s.FeedMode = FeedMode.UnitsPerMinute;
                    break;
                case G95:
                    Comment("interpreter: feed mode set to units per rev");
                    s.FeedMode = FeedMode.UnitsPerRev;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G93_OR_G94_OR_G95");
            }
            return 0;
        }

        // === feed rate (F…) ===

        private static int ConvertFeedRate(Block b, SetupData s)
        {
            SetFeedRate(b.FNumber);
            s.FeedRate = b.FNumber;
            return 0;
        }

        // === spindle mode (G96/G97) ===

        private static int ConvertSpindleMode(int gCode, SetupData s)
        {
            switch (gCode)
            {
                case G96:
                    SetSpindleMode(SpindleMode.Css);
                    s.SpindleMode = SpindleMode.Css;
                    break;
                case G97:
                    SetSpindleMode(SpindleMode.Normal);
                    s.SpindleMode = SpindleMode.Normal;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G96_OR_G97");
            }
            return 0;
        }

        // === spindle speed (S…) ===

        private static int ConvertSpeed(Block b, SetupData s)
        {
            SetSpindleSpeed(b.SNumber);
            s.SpindleSpeed = b.SNumber;
            return 0;
        }

        // === tool select (T…) ===

        private static int ConvertToolSelect(Block b, SetupData s)
        {
            // mirror C++: ConvertToolToIndex(settings, number, &index)
            int index = LookupToolIndex(s, (int)b.TNumber);
            s.SelectedToolSlot = index;
            return 0;
        }

        // === M-codes (tool change, spindle on/off, coolant, overrides…) ===

        private static int ConvertM(Block b, SetupData s)
        {
            // 1) Tool change (M6)
            if (b.Modes[6] != -1)
            {
                ConvertToolChange(s);
            }
            // 2) Spindle start/stop
            switch (b.Modes[7])
            {
                case 3:
                    StartSpindleClockwise();
                    s.SpindleTurning = SpindleTurning.Clockwise;
                    break;
                case 4:
                    StartSpindleCounterClockwise();
                    s.SpindleTurning = SpindleTurning.CounterClockwise;
                    break;
                case 5:
                    StopSpindleTurning();
                    s.SpindleTurning = SpindleTurning.Stopped;
                    break;
            }
            // 3) Coolant
            switch (b.Modes[8])
            {
                case 7:
                    MistOn();
                    s.Mist = CoolantState.On;
                    break;
                case 8:
                    FloodOn();
                    s.Flood = CoolantState.On;
                    break;
                case 9:
                    MistOff();
                    FloodOff();
                    s.Mist = CoolantState.Off;
                    s.Flood = CoolantState.Off;
                    break;
            }
            // 4) Overrides (M48/M49)
            if (b.Modes[9] == 48)
            {
                EnableFeedOverride();
                EnableSpeedOverride();
                s.FeedOverride = true;
                s.SpeedOverride = true;
            }
            else if (b.Modes[9] == 49)
            {
                if (b.PFlag && b.PNumber == 1)
                {
                    DisableFeedOverride();
                    EnableSpeedOverride();
                    s.FeedOverride = false;
                    s.SpeedOverride = true;
                }
                else if (b.PFlag && b.PNumber == 2)
                {
                    EnableFeedOverride();
                    DisableSpeedOverride();
                    s.FeedOverride = true;
                    s.SpeedOverride = false;
                }
                else if (b.PFlag)
                {
                    throw new InvalidOperationException("NCE_INVALID_PWORD_M49");
                }
                else
                {
                    DisableFeedOverride();
                    DisableSpeedOverride();
                    s.FeedOverride = false;
                    s.SpeedOverride = false;
                }
            }
            // optional M100, etc.
            if (b.Modes[10] != -1)
            {
                M100(b.Modes[10]);
            }
            return 0;
        }

        // === G-codes (all non-modal motions) ===

        private static int ConvertG(Block b, SetupData s)
        {
            // 1) dwell (G4)
            if (b.GModes[0] == G4)
                ConvertDwell(b.PNumber);

            // 2) plane select (G17/G18/G19)
            if (b.GModes[2] != -1)
                ConvertSetPlane(b.GModes[2], s);

            // 3) length units (G20/G21)
            if (b.GModes[6] != -1)
                ConvertLengthUnits(b.GModes[6], s);

            // 4) cutter comp (G40/G41/G42)
            if (b.GModes[7] != -1)
                ConvertCutterCompensation(b.GModes[7], b, s);

            // 5) tool length offset (G43/G49)
            if (b.GModes[8] != -1)
                ConvertToolLengthOffset(b.GModes[8], b, s);

            // 6) coordinate system (G54–G59.s)
            if (b.GModes[12] != -1)
                ConvertCoordinateSystem(b.GModes[12], s);

            // 7) control mode (G61/G61.1/G64)
            if (b.GModes[13] != -1)
                ConvertControlMode(b.GModes[13], s);

            // 8) distance mode (G90/G91)
            if (b.GModes[3] != -1)
                ConvertDistanceMode(b.GModes[3], s);

            // 9) retract mode (G98/G99)
            if (b.GModes[10] != -1)
                ConvertRetractMode(b.GModes[10], s);

            // 10) modal-0 codes (G10, G28, G30, G92, …)
            if (b.GModes[0] != -1)
                ConvertModal0(b.GModes[0], b, s);

            // 11) any implicit or explicit motion (G0, G1, G2, G3, canned, etc.)
            if (b.MotionToBe != -1)
                ConvertMotion(b.MotionToBe, b, s);

            return 0;
        }

        // === stopping codes (M0, M1, M2, M30, M60) ===

        private static int ConvertStop(Block b, SetupData s)
        {
            int m = b.Modes[0];
            if (m == 0 || m == 1 || m == 2 || m == 30 || m == 60)
                return EXIT_CODE;    // signal interpreter exit
            else
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_M0_M1_M2_M30_M60");
        }

        // === the simple helpers from convert_g ===

        private static int ConvertControlMode(int gCode, SetupData s)
        {
            switch (gCode)
            {
                case G61:
                    SetMotionControlMode(MotionControl.ExactPath);
                    s.ControlMode = MotionControl.ExactPath;
                    break;
                case G61_1:
                    SetMotionControlMode(MotionControl.ExactStop);
                    s.ControlMode = MotionControl.ExactStop;
                    break;
                case G64:
                    SetMotionControlMode(MotionControl.Continuous);
                    s.ControlMode = MotionControl.Continuous;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G61_G61_1_OR_G64");
            }
            return 0;
        }

        private static int ConvertDistanceMode(int gCode, SetupData s)
        {
            switch (gCode)
            {
                case G90:
                    s.DistanceMode = DistanceMode.Absolute;
                    break;
                case G91:
                    s.DistanceMode = DistanceMode.Incremental;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G90_OR_G91");
            }
            return 0;
        }

        private static int ConvertDwell(double time)
        {
            Dwell(time);
            return 0;
        }

        private static int ConvertLengthUnits(int gCode, SetupData s)
        {
            switch (gCode)
            {
                case G20:
                    s.LengthUnits = LengthUnits.Inches;
                    break;
                case G21:
                    s.LengthUnits = LengthUnits.Millimeters;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G20_OR_G21");
            }
            return 0;
        }

        private static int ConvertModal0(int code, Block b, SetupData s)
        {
            switch (code)
            {
                case G10:
                    ConvertSetup(b, s);
                    break;
                case G28:
                case G30:
                    ConvertHome(code, b, s);
                    break;
                case G92:
                case G92_1:
                case G92_2:
                case G92_3:
                case G52:
                    ConvertAxisOffsets(code, b, s);
                    break;
                // G4 and G53 handled elsewhere
                case G4:
                case G53:
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G4_G10_G28_G30_G53_OR_G92_SERIES");
            }
            return 0;
        }



        /// <summary>
        /// G10: Set a program‐origin offset for the selected CS. :contentReference[oaicite:0]{index=0}</summary>
        private static int ConvertSetup(Block b, SetupData s)
        {
            int pInt = (int)(b.p_number + 0.0001);
            double[] pars = s.parameters;

            // X
            double x = b.x_flag ? b.x_number : pars[5201 + pInt * 20];
            if (b.x_flag) pars[PChanged(5201 + pInt * 20)] = x;
            // Y
            double y = b.y_flag ? b.y_number : pars[5202 + pInt * 20];
            if (b.y_flag) pars[PChanged(5202 + pInt * 20)] = y;
            // Z
            double z = b.z_flag ? b.z_number : pars[5203 + pInt * 20];
            if (b.z_flag) pars[PChanged(5203 + pInt * 20)] = z;
            // A
            double a = b.a_flag ? b.a_number : pars[5204 + pInt * 20];
            if (b.a_flag) pars[PChanged(5204 + pInt * 20)] = a;
            // B
            double bb = b.b_flag ? b.b_number : pars[5205 + pInt * 20];
            if (b.b_flag) pars[PChanged(5205 + pInt * 20)] = bb;
            // C
            double c = b.c_flag ? b.c_number : pars[5206 + pInt * 20];
            if (b.c_flag) pars[PChanged(5206 + pInt * 20)] = c;
            // U
            double u = b.u_flag ? b.u_number : pars[5207 + pInt * 20];
            if (b.u_flag) pars[PChanged(5207 + pInt * 20)] = u;
            // V
            double v = b.v_flag ? b.v_number : pars[5208 + pInt * 20];
            if (b.v_flag) pars[PChanged(5208 + pInt * 20)] = v;

            // If this is the active CS, adjust current_* and origin_offset_*
            if (pInt == s.origin_index)
            {
                s.current_x += s.origin_offset_x;
                s.current_y += s.origin_offset_y;
                s.current_z += s.origin_offset_z;
                s.AA_current += s.AA_origin_offset;
                s.BB_current += s.BB_origin_offset;
                s.CC_current += s.CC_origin_offset;
                s.UU_current += s.UU_origin_offset;
                s.VV_current += s.VV_origin_offset;

                // set new origin
                s.origin_offset_x = x;
                s.origin_offset_y = y;
                s.origin_offset_z = z;
                s.AA_origin_offset = a;
                s.BB_origin_offset = bb;
                s.CC_origin_offset = c;
                s.UU_origin_offset = u;
                s.VV_origin_offset = v;

                s.current_x -= x;
                s.current_y -= y;
                s.current_z -= z;
                s.AA_current -= a;
                s.BB_current -= bb;
                s.CC_current -= c;
                s.UU_current -= u;
                s.VV_current -= v;

                // Emit the canonical “SET_ORIGIN_OFFSETS”
                SetOriginOffsets(
                    x + s.axis_offset_x,
                    y + s.axis_offset_y,
                    z + s.axis_offset_z,
                    a + s.AA_axis_offset,
                    bb + s.BB_axis_offset,
                    c + s.CC_axis_offset,
                    u + s.UU_axis_offset,
                    v + s.VV_axis_offset
                );
            }
            return RS274NGC_OK;
        }

        /// <summary>
        /// G28/G30: Go to an intermediate point then home. :contentReference[oaicite:1]{index=1}</summary>
        private static int ConvertHome(int move, Block b, SetupData s)
        {
            // compute ends of first segment
            find_ends(b, s,
                out double endX, out double endY, out double endZ,
                out double AA_end, out double BB_end, out double CC_end,
                out double UU_end, out double VV_end);

            if (s.cutter_comp_side != CutterCompensation.Off)
                throw new InvalidOperationException("NCE_CANNOT_USE_G28_OR_G30_WITH_CUTTER_RADIUS_COMP");

            // rapid‐traverse to block point
            StraightTraverse(endX, endY, endZ, AA_end, BB_end, CC_end, UU_end, VV_end);

            // then to reference 1 (G28) or ref 2 (G30)
            if (move == G_28)
                find_relative(
                    s.parameters[5161], s.parameters[5162], s.parameters[5163],
                    s.parameters[5164], /*AA*/s.parameters[5165], /*BB*/s.parameters[5166],
                    s.parameters[5167], /*UU*/s.parameters[5168], /*VV*/s.parameters[5169],
                    out endX, out endY, out endZ,
                    out AA_end, out BB_end, out CC_end, out UU_end, out VV_end,
                    s
                );
            else if (move == G_30)
                find_relative(
                    s.parameters[5181], s.parameters[5182], s.parameters[5183],
                    s.parameters[5184], /*AA*/s.parameters[5185], /*BB*/s.parameters[5186],
                    s.parameters[5187], /*UU*/s.parameters[5188], /*VV*/s.parameters[5189],
                    out endX, out endY, out endZ,
                    out AA_end, out BB_end, out CC_end, out UU_end, out VV_end,
                    s
                );
            else
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_G28_OR_G30");

            // rapid‐traverse to home and update current_*
            StraightTraverse(endX, endY, endZ, AA_end, BB_end, CC_end, UU_end, VV_end);
            s.current_x = endX; s.current_y = endY; s.current_z = endZ;
            s.AA_current = AA_end; s.BB_current = BB_end; s.CC_current = CC_end;
            s.UU_current = UU_end; s.VV_current = VV_end;

            return RS274NGC_OK;
        }

        /// <summary>
        /// G40/G41/G42: Tool‐radius compensation. :contentReference[oaicite:2]{index=2}</summary>
        private static int ConvertCutterCompensation(int gCode, Block b, SetupData s)
        {
            switch (gCode)
            {
                case G_40:
                    ConvertCutterCompensationOff(s);
                    break;
                case G_41:
                    ConvertCutterCompensationOn(CutterComp.Left, b, s);
                    break;
                case G_42:
                    ConvertCutterCompensationOn(CutterComp.Right, b, s);
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G40_G41_OR_G42");
            }
            return RS274NGC_OK;
        }

        private static int ConvertCutterCompensationOff(SetupData s)
        {
            // interpreter comment omitted in release
            s.cutter_comp_side = CutterComp.Off;
            if (s.program_x != double.NaN)
            {
                s.current_x = s.program_x;
                s.current_y = s.program_y;
                s.program_x = double.NaN;
                s.pending_comp_x = double.NaN;
            }
            return RS274NGC_OK;
        }

        private static int ConvertCutterCompensationOn(CutterComp side, Block b, SetupData s)
        {
            if (s.plane != CanonPlane.XY)
                throw new InvalidOperationException("NCE_CANNOT_TURN_CUTTER_RADIUS_COMP_ON_OUT_OF_XY_PLANE");
            if (s.cutter_comp_side != CutterComp.Off)
                throw new InvalidOperationException("NCE_CANNOT_TURN_CUTTER_RADIUS_COMP_ON_WHEN_ON");

            // set up compensation using the current tool table diameter
            double radius = s.tool_table[s.selected_tool_slot].Diameter / 2.0;
            s.cutter_comp_radius = radius;
            s.cutter_comp_side = side;
            s.program_x = s.current_x;  // remember un‐compensated
            s.program_y = s.current_y;
            s.pending_comp_x = double.NaN;
            return RS274NGC_OK;
        }

        /// <summary>
        /// G98/G99: Retract mode for canned cycles. :contentReference[oaicite:3]{index=3}</summary>
        private static int ConvertRetractMode(int gCode, SetupData s)
        {
            if (gCode == G_98) s.retract_mode = RetractMode.OldZ;
            else if (gCode == G_99) s.retract_mode = RetractMode.RPlane;
            else throw new InvalidOperationException("NCE_BUG_CODE_NOT_G98_OR_G99");
            return RS274NGC_OK;
        }

        /// <summary>
        /// G17/G18/G19: Plane select. :contentReference[oaicite:4]{index=4}</summary>
        private static int ConvertSetPlane(int gCode, SetupData s)
        {
            if (gCode == G_17)
            {
                SelectPlane(CANON_PLANE.XY);
                s.plane = CANON_PLANE.XY;
            }
            else if (gCode == G_18)
            {
                if (s.cutter_comp_side != CutterComp.Off)
                    throw new InvalidOperationException("NCE_CANNOT_USE_XZ_PLANE_WITH_CUTTER_RADIUS_COMP");
                SelectPlane(CANON_PLANE.XZ);
                s.plane = CANON_PLANE.XZ;
            }
            else if (gCode == G_19)
            {
                if (s.cutter_comp_side != CutterComp.Off)
                    throw new InvalidOperationException("NCE_CANNOT_USE_YZ_PLANE_WITH_CUTTER_RADIUS_COMP");
                SelectPlane(CANON_PLANE.YZ);
                s.plane = CANON_PLANE.YZ;
            }
            else
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_G17_G18_OR_G19");
            return RS274NGC_OK;
        }

        /// <summary>
        /// G54 – G59.3: Coordinate-system select. :contentReference[oaicite:5]{index=5}</summary>
        private static int ConvertCoordinateSystem(int gCode, SetupData s)
        {
            int origin = gCode switch
            {
                G_54 => 1,
                G_55 => 2,
                G_56 => 3,
                G_57 => 4,
                G_58 => 5,
                G_59 => 6,
                G_59_1 => 7,
                G_59_2 => 8,
                G_59_3 => 9,
                _ => throw new InvalidOperationException("NCE_BUG_CODE_NOT_IN_RANGE_G54_TO_G593")
            };

            // if already in that CS with same units, no-op
            if (s.origin_index == origin && s.length_units_of_origin == s.length_units)
                return RS274NGC_OK;

            s.origin_index = origin;
            s.length_units_of_origin = s.length_units;
            s.parameters[PChanged(5220)] = origin;

            // shift current_* by old origin, then subtract new origin
            s.current_x += s.origin_offset_x;
            s.current_y += s.origin_offset_y;
            s.current_z += s.origin_offset_z;
            s.AA_current += s.AA_origin_offset;
            s.BB_current += s.BB_origin_offset;
            s.CC_current += s.CC_origin_offset;
            s.UU_current += s.UU_origin_offset;
            s.VV_current += s.VV_origin_offset;

            // load new origin offsets from parameters
            int i = (origin - 1) * 20;
            s.origin_offset_x = s.parameters[5201 + i];
            s.origin_offset_y = s.parameters[5202 + i];
            s.origin_offset_z = s.parameters[5203 + i];
            s.AA_origin_offset = s.parameters[5204 + i];
            s.BB_origin_offset = s.parameters[5205 + i];
            s.CC_origin_offset = s.parameters[5206 + i];
            s.UU_origin_offset = s.parameters[5207 + i];
            s.VV_origin_offset = s.parameters[5208 + i];

            // subtract off new origin
            s.current_x -= s.origin_offset_x;
            s.current_y -= s.origin_offset_y;
            s.current_z -= s.origin_offset_z;
            s.AA_current -= s.AA_origin_offset;
            s.BB_current -= s.BB_origin_offset;
            s.CC_current -= s.CC_origin_offset;
            s.UU_current -= s.UU_origin_offset;
            s.VV_current -= s.VV_origin_offset;

            // canonical “SET_ORIGIN_OFFSETS”
            SetOriginOffsets(
                s.origin_offset_x + s.axis_offset_x,
                s.origin_offset_y + s.axis_offset_y,
                s.origin_offset_z + s.axis_offset_z,
                s.AA_origin_offset + s.AA_axis_offset,
                s.BB_origin_offset + s.BB_axis_offset,
                s.CC_origin_offset + s.CC_axis_offset,
                s.UU_origin_offset + s.UU_axis_offset,
                s.VV_origin_offset + s.VV_axis_offset
            );
            return RS274NGC_OK;
        }

        /// <summary>
        /// Dispatch any motion: G0/G1/G2/G3/G38.2/G80/G81–G89. :contentReference[oaicite:7]{index=7}</summary>
        private static int ConvertMotion(int motion, Block b, SetupData s)
        {
            switch (motion)
            {
                case G_0:  // rapid‐traverse
                case G_1:  // feed
                           // distance vs inverse‐time handled upstream
                    return RS274NGC_OK;
                case G_2:
                case G_3:
                    ConvertArc(motion, b, s);
                    return RS274NGC_OK;
                case G_38_2:
                    ConvertProbe(b, s);
                    return RS274NGC_OK;
                case G_80:
                    // Cancel canned cycle: no‐op
                    return RS274NGC_OK;
                default:
                    if (motion >= G_81 && motion <= G_89)
                    {
                        ConvertCycle(motion, b, s);
                        return RS274NGC_OK;
                    }
                    throw new InvalidOperationException("NCE_BUG_UNKNOWN_MOTION_CODE");
            }
        }

        /// <summary>
        /// G38.2: Single‐point probe with straight‐feed. :contentReference[oaicite:8]{index=8}</summary>
        private static int ConvertProbe(Block b, SetupData s)
        {
            if (s.feed_mode == FeedMode.InverseTime)
                throw new InvalidOperationException("NCE_CANNOT_PROBE_IN_INVERSE_TIME_FEED_MODE");
            if (s.cutter_comp_side != CutterComp.Off)
                throw new InvalidOperationException("NCE_CANNOT_PROBE_WITH_CUTTER_RADIUS_COMP_ON");
            if (s.feed_rate == 0.0)
                throw new InvalidOperationException("NCE_CANNOT_PROBE_WITH_ZERO_FEED_RATE");

            // Ensure no rotary movement
            find_ends(b, s,
                out double ex, out double ey, out double ez,
                out double eA, out double eB, out double eC,
                out double eU, out double eV
            );
            double dist = Math.Sqrt(
                Math.Pow(s.current_x - ex, 2) +
                Math.Pow(s.current_y - ey, 2) +
                Math.Pow(s.current_z - ez, 2)
            );
            if (dist < ((s.length_units == Units.Millimeters) ? 0.254 : 0.01))
                throw new InvalidOperationException("NCE_START_POINT_TOO_CLOSE_TO_PROBE_POINT");

            TurnProbeOn();
            StraightProbe(ex, ey, ez, eA, eB, eC, eU, eV);
            TurnProbeOff();
            s.motion_mode = G_38_2;
            s.probe_flag = true;
            return RS274NGC_OK;
        }

        /// <summary>
        /// G81–G89: Canned‐cycle dispatch. :contentReference[oaicite:9]{index=9}</summary>
        private static int ConvertCycle(int motion, Block b, SetupData s)
        {
            // you would dispatch into G81, G82…G89 here, e.g.:
            if (motion == G_81) ConvertCycleG81(s.plane, b.x_number, b.y_number, b.clear_z, b.bottom_z);
            else if (motion == G_82) ConvertCycleG82(s.plane, b.x_number, b.y_number, b.clear_z, b.bottom_z, b.p_number);
            // … etc. through G_89 …
            else throw new InvalidOperationException("NCE_BUG_UNKNOWN_CANNED_CYCLE");
            return RS274NGC_OK;
        }

        /// <summary>
        /// G76/G32: Threading or custom cycles. :contentReference[oaicite:10]{index=10}</summary>
        private static int ConvertThread(int motion, Block b, SetupData s)
        {
            // G32—single‐point threading:
            if (motion == G_32)
                ConvertCycleG32(s.plane, b.x_number, b.z_number, b.clear_z);
            // G76—multi‐pass threading (example):
            else if (motion == G_76)
                ConvertCycleG76(s.plane, b.x_number, b.z_number, b.r_number, b.p_number, b.q_number);
            else
                throw new InvalidOperationException("NCE_BUG_UNKNOWN_THREAD_CYCLE");
            return RS274NGC_OK;
        }
        private static int FindEnds(
            Block b, SetupData s,
            out double px, out double py, out double pz,
            out double aa, out double bb, out double cc,
            out double uu, out double vv)
        {
            int mode = (int)s.DistanceMode;
            bool middle = !double.IsNaN(s.ProgramX);
            bool comp = s.CutterCompSide != CutterComp.Off;

            // G53: machine coordinates
            if (b.GModes[0] == G_53)
            {
                px = b.XFlag
                    ? b.XNumber - (s.ToolXOffset + s.OriginOffsetX + s.AxisOffsetX)
                    : s.CurrentX;
                py = b.YFlag
                    ? b.YNumber - (s.ToolYOffset + s.OriginOffsetY + s.AxisOffsetY)
                    : s.CurrentY;
                pz = b.ZFlag
                    ? b.ZNumber - (s.ToolLengthOffset + s.OriginOffsetZ + s.AxisOffsetZ)
                    : s.CurrentZ;
                aa = b.AFlag
                    ? b.ANumber - (s.AAOriginOffset + s.AAAxisOffset)
                    : s.AACurrent;
                bb = b.BFlag
                    ? b.BNumber - (s.BBOriginOffset + s.BBAxisOffset)
                    : s.BBCurrent;
                cc = b.CFlag
                    ? b.CNumber - (s.CCOriginOffset + s.CCAxisOffset)
                    : s.CCCurrent;
                uu = b.UFlag
                    ? b.UNumber - (s.UUOriginOffset + s.UUAxisOffset)
                    : s.UUCurrent;
                vv = b.VFlag
                    ? b.VNumber - (s.VVOriginOffset + s.VVAxisOffset)
                    : s.VVCurrent;
            }
            // Absolute mode
            else if (mode == (int)DistanceMode.Absolute)
            {
                px = b.XFlag
                    ? b.XNumber
                    : (comp && middle ? s.ProgramX : s.CurrentX);
                py = b.YFlag
                    ? b.YNumber
                    : (comp && middle ? s.ProgramY : s.CurrentY);
                pz = b.ZFlag
                    ? b.ZNumber
                    : (comp && middle ? s.ProgramZ : s.CurrentZ);
                aa = b.AFlag ? b.ANumber : s.AACurrent;
                bb = b.BFlag ? b.BNumber : s.BBCurrent;
                cc = b.CFlag ? b.CNumber : s.CCCurrent;
                uu = b.UFlag ? b.UNumber : s.UUCurrent;
                vv = b.VFlag ? b.VNumber : s.VVCurrent;
            }
            // Incremental mode
            else
            {
                px = b.XFlag
                    ? (comp && middle ? b.XNumber + s.ProgramX : b.XNumber + s.CurrentX)
                    : (comp && middle ? s.ProgramX : s.CurrentX);
                py = b.YFlag
                    ? (comp && middle ? b.YNumber + s.ProgramY : b.YNumber + s.CurrentY)
                    : (comp && middle ? s.ProgramY : s.CurrentY);
                pz = b.ZFlag
                    ? (s.CurrentZ + b.ZNumber)
                    : s.CurrentZ;
                aa = b.AFlag ? (s.AACurrent + b.ANumber) : s.AACurrent;
                bb = b.BFlag ? (s.BBCurrent + b.BNumber) : s.BBCurrent;
                cc = b.CFlag ? (s.CCCurrent + b.CNumber) : s.CCCurrent;
                uu = b.UFlag ? (s.UUCurrent + b.UNumber) : s.UUCurrent;
                vv = b.VFlag ? (s.VVCurrent + b.VNumber) : s.VVCurrent;
            }

            return RS274NGC_OK;
        }

        /// <summary>
        /// Convert an absolute point (x1…vv1) into a relative point under current tool offsets. :contentReference[oaicite:1]{index=1}</summary>
        private static int FindRelative(
            double x1, double y1, double z1,
            double aa1, double bb1, double cc1,
            double uu1, double vv1,
            out double x2, out double y2, out double z2,
            out double aa2, out double bb2, out double cc2,
            out double uu2, out double vv2,
            SetupData s)
        {
            x2 = x1 - (s.ToolXOffset + s.OriginOffsetX + s.AxisOffsetX);
            y2 = y1 - (s.ToolYOffset + s.OriginOffsetY + s.AxisOffsetY);
            z2 = z1 - (s.ToolLengthOffset + s.OriginOffsetZ + s.AxisOffsetZ);

            aa2 = aa1 - (s.AAOriginOffset + s.AAAxisOffset);
            bb2 = bb1 - (s.BBOriginOffset + s.BBAxisOffset);
            cc2 = cc1 - (s.CCOriginOffset + s.CCAxisOffset);
            uu2 = uu1 - (s.UUOriginOffset + s.UUAxisOffset);
            vv2 = vv1 - (s.VVOriginOffset + s.VVAxisOffset);

            return RS274NGC_OK;
        }

        /// <summary>
        /// Implements G52/G92 axis‐offset logic exactly as in the C++ reference. :contentReference[oaicite:2]{index=2}</summary>
        private static int ConvertAxisOffsets(int gCode, Block b, SetupData s)
        {
            if (s.CutterCompSide != CutterComp.Off)
                throw new InvalidOperationException("NCE_CANNOT_CHANGE_AXIS_OFFSETS_WITH_CUTTER_RADIUS_COMP");

            // Helper to mark a parameter dirty
            int PChanged(int idx)
            {
                s.ParamChanges[s.n_ParamChanges++] = idx;
                return idx;
            }

            if (gCode == G_92)
            {
                // Incremental origin shift
                if (b.XFlag)
                {
                    s.AxisOffsetX = s.CurrentX + s.AxisOffsetX - b.XNumber;
                    s.CurrentX = b.XNumber;
                    s.parameters[PChanged(5211)] = s.AxisOffsetX;
                }
                if (b.YFlag)
                {
                    s.AxisOffsetY = s.CurrentY + s.AxisOffsetY - b.YNumber;
                    s.CurrentY = b.YNumber;
                    s.parameters[PChanged(5212)] = s.AxisOffsetY;
                }
                if (b.ZFlag)
                {
                    s.AxisOffsetZ = s.CurrentZ + s.AxisOffsetZ - b.ZNumber;
                    s.CurrentZ = b.ZNumber;
                    s.parameters[PChanged(5213)] = s.AxisOffsetZ;
                }
                if (b.AFlag)
                {
                    s.AAAxisOffset = s.AACurrent + s.AAAxisOffset - b.ANumber;
                    s.AACurrent = b.ANumber;
                    s.parameters[PChanged(5214)] = s.AAAxisOffset;
                }
                if (b.BFlag)
                {
                    s.BBAxisOffset = s.BBCurrent + s.BBAxisOffset - b.BNumber;
                    s.BBCurrent = b.BNumber;
                    s.parameters[PChanged(5215)] = s.BBAxisOffset;
                }
                if (b.CFlag)
                {
                    s.CCAxisOffset = s.CCCurrent + s.CCAxisOffset - b.CNumber;
                    s.CCCurrent = b.CNumber;
                    s.parameters[PChanged(5216)] = s.CCAxisOffset;
                }

                // propagate origin offsets and emit
                SetOriginOffsets(
                    s.OriginOffsetX + s.AxisOffsetX,
                    s.OriginOffsetY + s.AxisOffsetY,
                    s.OriginOffsetZ + s.AxisOffsetZ,
                    s.AAOriginOffset + s.AAAxisOffset,
                    s.BBOriginOffset + s.BBAxisOffset,
                    s.CCOriginOffset + s.CCAxisOffset,
                    s.UUOriginOffset + s.UUAxisOffset,
                    s.VVOriginOffset + s.VVAxisOffset
                );
            }
            else if (gCode == G_52)
            {
                // Absolute axis‐offset set
                if (b.XFlag) { s.AxisOffsetX = b.XNumber; s.CurrentX = s.CurrentX; }
                if (b.YFlag) { s.AxisOffsetY = b.YNumber; s.CurrentY = s.CurrentY; }
                if (b.ZFlag) { s.AxisOffsetZ = b.ZNumber; s.CurrentZ = s.CurrentZ; }
                if (b.AFlag) { s.AAAxisOffset = b.ANumber; s.AACurrent = s.AACurrent; }
                if (b.BFlag) { s.BBAxisOffset = b.BNumber; s.BBCurrent = s.BBCurrent; }
                if (b.CFlag) { s.CCAxisOffset = b.CNumber; s.CCCurrent = s.CCCurrent; }

                SetOriginOffsets(
                    s.OriginOffsetX + s.AxisOffsetX,
                    s.OriginOffsetY + s.AxisOffsetY,
                    s.OriginOffsetZ + s.AxisOffsetZ,
                    s.AAOriginOffset + s.AAAxisOffset,
                    s.BBOriginOffset + s.BBAxisOffset,
                    s.CCOriginOffset + s.CCAxisOffset,
                    s.UUOriginOffset + s.UUAxisOffset,
                    s.VVOriginOffset + s.VVAxisOffset
                );
            }
            else
            {
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_G52_OR_G92");
            }

            return RS274NGC_OK;
        }

        // --- Canned‐cycle primitives (G81–G89) ---

        /// <summary>G81: simple drill. :contentReference[oaicite:3]{index=3}</summary>
        private static int ConvertCycleG81(CANON_PLANE plane, double x, double y, double clearZ, double bottomZ)
        {
            CycleFeed(plane, x, y, bottomZ);
            CycleTraverse(plane, x, y, clearZ);
            return RS274NGC_OK;
        }

        /// <summary>G82: drill + dwell. :contentReference[oaicite:4]{index=4}</summary>
        private static int ConvertCycleG82(CANON_PLANE plane, double x, double y, double clearZ, double bottomZ, double dwell)
        {
            CycleFeed(plane, x, y, bottomZ);
            Dwell(dwell);
            CycleTraverse(plane, x, y, clearZ);
            return RS274NGC_OK;
        }

        /// <summary>G83: peck drilling. :contentReference[oaicite:5]{index=5}</summary>
        private static int ConvertCycleG83(CANON_PLANE plane, double x, double y, double r, double clearZ, double bottomZ, double delta)
        {
            double rapidDelta = Math.Max(0.0, delta);
            double currentDepth = r - delta;
            while (currentDepth > bottomZ)
            {
                CycleFeed(plane, x, y, currentDepth);
                Dwell(s => s.CycleP);  // uses last P
                CycleTraverse(plane, x, y, r);
                CycleTraverse(plane, x, y, currentDepth + rapidDelta);
                if (GetAbort()) return RS274NGC_EXIT;
                currentDepth -= delta;
            }
            CycleFeed(plane, x, y, bottomZ);
            CycleTraverse(plane, x, y, clearZ);
            return RS274NGC_OK;
        }

        /// <summary>G84–G89:</summary>

        // -------------------------------------------------------------------------------------------------
        // ENHANCE_BLOCK  (C++: static int enhance_block) :contentReference[oaicite:7]{index=7}
        // -------------------------------------------------------------------------------------------------
        private static int EnhanceBlock(Block block, SetupData settings)
        {
            bool axisFlag =
                block.XFlag || block.YFlag ||
                block.ZFlag || block.AFlag ||
                block.BFlag || block.CFlag ||
                block.UFlag || block.VFlag;

            int mode0 = block.GModes[0];
            int mode1 = block.GModes[1];
            bool modeZeroCovetsAxes =
                mode0 == G10 ||
                mode0 == G28 ||
                mode0 == G30 ||
                mode0 == G92 ||
                mode0 == G92_3 ||
                mode0 == G52;

            if (mode1 != -1)
            {
                if (mode1 == G80)
                {
                    if (axisFlag && !modeZeroCovetsAxes)
                        return NCE_CANNOT_USE_AXIS_VALUES_WITH_G80;
                    if (!axisFlag && (mode0 == G92 || mode0 == G52))
                        return NCE_ALL_AXES_MISSING_WITH_G52_G92;
                }
                else
                {
                    if (modeZeroCovetsAxes)
                        return NCE_CANNOT_USE_TWO_G_CODES_THAT_BOTH_USE_AXIS_VALUES;
                    // note: original C++ commented-out the “all axes missing” for motion codes here :contentReference[oaicite:8]{index=8}
                }
                block.MotionToBe = mode1;
            }
            else if (modeZeroCovetsAxes)
            {
                if (!axisFlag && (mode0 == G92 || mode0 == G52))
                    return NCE_ALL_AXES_MISSING_WITH_G52_G92;
            }
            else if (
                axisFlag ||
                ((settings.MotionMode == G2 || settings.MotionMode == G3) &&
                 (block.IFlag || block.JFlag || block.KFlag))
            )
            {
                if (settings.MotionMode == -1 || settings.MotionMode == G80)
                    return NCE_CANNOT_USE_AXIS_VALUES_WITHOUT_A_G_CODE_THAT_USES_THEM;
                block.MotionToBe = settings.MotionMode;
            }

            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CHECK_ITEMS  (C++: static int check_items) :contentReference[oaicite:9]{index=9}
        // -------------------------------------------------------------------------------------------------
        private static int CheckItems(Block block, SetupData settings)
        {
            int status;
            if ((status = CheckGCodes(block, settings)) != RS274NGC_OK) return status;
            if ((status = CheckMCodes(block)) != RS274NGC_OK) return status;
            if ((status = CheckOtherCodes(block)) != RS274NGC_OK) return status;
            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CHECK_M_CODES  (C++: static int check_m_codes) :contentReference[oaicite:10]{index=10}
        // -------------------------------------------------------------------------------------------------
        private static int CheckMCodes(Block block)
        {
            // 1. Too many M codes on one line
            if (block.MCount > MAX_EMS)
                return NCE_TOO_MANY_M_CODES_ON_LINE;

            // 2. M98 loop parameters
            if (block.MModes[4] == 98)  // mode 4 == M98
            {
                int pInt = (int)(block.PNumber + 0.0001);
                if (block.PNumber < 0.0)
                    return NCE_NEGATIVE_P_WORD_USED;
                if ((block.PNumber + 0.0001 - pInt) > 0.0002)
                    return NCE_P_VALUE_NOT_AN_INTEGER_WITH_G10_L2_M98;

                // default Q=1 if neither Q nor L given
                if (!block.QFlag && !block.LFlag)
                {
                    block.QNumber = 1.0;
                    block.QFlag = true;
                }

                int qInt = (int)(block.QNumber + 0.0001);
                if ((block.QNumber + 0.0001 - qInt) > 0.0002)
                    return NCE_Q_VALUE_NOT_AN_INTEGER_WITH_M98;

                int lInt = (int)(block.LNumber + 0.0001);
                if ((block.LNumber + 0.0001 - lInt) > 0.0002)
                    return NCE_L_VALUE_NOT_AN_INTEGER_WITH_M98;
            }

            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CHECK_OTHER_CODES  (C++: static int check_other_codes) :contentReference[oaicite:11]{index=11}
        // -------------------------------------------------------------------------------------------------
        private static int CheckOtherCodes(Block block)
        {
            int motion = block.MotionToBe;

            // A, B, C, U, V not allowed in canned cycles (G81–G89)
            if (block.AFlag)
                if (block.GModes[1] > G80 && block.GModes[1] < G90)
                    return NCE_CANNOT_PUT_AN_A_IN_CANNED_CYCLE;
            if (block.BFlag)
                if (block.GModes[1] > G80 && block.GModes[1] < G90)
                    return NCE_CANNOT_PUT_A_B_IN_CANNED_CYCLE;
            if (block.CFlag)
                if (block.GModes[1] > G80 && block.GModes[1] < G90)
                    return NCE_CANNOT_PUT_A_C_IN_CANNED_CYCLE;
            if (block.UFlag)
                if (block.GModes[1] > G80 && block.GModes[1] < G90)
                    return NCE_CANNOT_PUT_A_U_IN_CANNED_CYCLE;
            if (block.VFlag)
                if (block.GModes[1] > G80 && block.GModes[1] < G90)
                    return NCE_CANNOT_PUT_A_V_IN_CANNED_CYCLE;

            // I, J, K only with arcs or G87
            if (block.IFlag && motion != G2 && motion != G3 && motion != G87)
                return NCE_I_SPECIFIED_IN_G_CODE_THAT_DOES_NOT_USE_IT;
            if (block.JFlag && motion != G2 && motion != G3 && motion != G87)
                return NCE_J_SPECIFIED_IN_G_CODE_THAT_DOES_NOT_USE_IT;
            if (block.KFlag && motion != G2 && motion != G3 && motion != G87)
                return NCE_K_SPECIFIED_IN_G_CODE_THAT_DOES_NOT_USE_IT;

            // P only with G4, G10, G82, G83, G86, G88, G89, M98
            if (block.PFlag && !(
                block.GModes[0] == G4 || block.GModes[0] == G10 ||
                block.GModes[1] == G82 || block.GModes[1] == G83 ||
                block.GModes[1] == G86 || block.GModes[1] == G88 ||
                block.GModes[1] == G89 ||
                block.MModes[4] == 98))
                return NCE_P_WORD_WITH_NO_G4_G10_G82_G86_G88_G89_M98_M100_119;

            // Q only with G83
            if (block.QFlag && block.GModes[1] != G83)
                return NCE_Q_WORD_WITH_NO_G83;

            // R only with G codes or M98/M100-119
            if (block.RFlag && !(
                block.GModes[0] == G10 || block.GModes[1] == G2 ||
                block.GModes[1] == G3 ||
                (block.GModes[1] > G80 && block.GModes[1] < G90) ||
                block.MModes[4] == 98))
                return NCE_R_WORD_WITH_NO_G_CODE_THAT_USES_IT;

            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CYCLE_FEED / CYCLE_TRAVERSE  (C++: cycle_feed, cycle_traverse) 
        // -------------------------------------------------------------------------------------------------
        private static int CycleFeed(CANON_PLANE plane, double e1, double e2, double e3)
        {
            switch (plane)
            {
                case CanonPlane.XY:
                    STRAIGHT_FEED(e1, e2, e3,
                        _setup.AACurrent, _setup.BBCurrent, _setup.CCCurrent,
                        _setup.UUCurrent, _setup.VVCurrent);
                    break;
                case CanonPlane.YZ:
                    STRAIGHT_FEED(e3, e1, e2,
                        _setup.AACurrent, _setup.BBCurrent, _setup.CCCurrent,
                        _setup.UUCurrent, _setup.VVCurrent);
                    break;
                default: // XZ
                    STRAIGHT_FEED(e2, e3, e1,
                        _setup.AACurrent, _setup.BBCurrent, _setup.CCCurrent,
                        _setup.UUCurrent, _setup.VVCurrent);
                    break;
            }
            return RS274NGC_OK;
        }

        private static int CycleTraverse(CANON_PLANE plane, double e1, double e2, double e3)
        {
            switch (plane)
            {
                case CanonPlane.XY:
                    STRAIGHT_TRAVERSE(e1, e2, e3,
                        _setup.AACurrent, _setup.BBCurrent, _setup.CCCurrent,
                        _setup.UUCurrent, _setup.VVCurrent);
                    break;
                case CanonPlane.YZ:
                    STRAIGHT_TRAVERSE(e3, e1, e2,
                        _setup.AACurrent, _setup.BBCurrent, _setup.CCCurrent,
                        _setup.UUCurrent, _setup.VVCurrent);
                    break;
                default: // XZ
                    STRAIGHT_TRAVERSE(e2, e3, e1,
                        _setup.AACurrent, _setup.BBCurrent, _setup.CCCurrent,
                        _setup.UUCurrent, _setup.VVCurrent);
                    break;
            }
            return RS274NGC_OK;
        }
        /// <summary>
        /// Top‐level G₂/G₃ converter. Computes arc parameters then emits the arc feed. :contentReference[oaicite:1]{index=1}</summary>
        private static int ConvertArc(Block b, SetupData s)
        {
            // compute arc: fe/final-end coords, se/start coords, fa/final-center coords, sa/start-center coords
            int status = ArcData(b.MotionToBe, b, s,
                                 out double fe, out double se,
                                 out double fa, out double sa,
                                 out int dir, out double ae);
            if (status != RS274NGC_OK) return status;

            // emit the feed‐arc command
            // ARC_FEED(x_start, y_start, z_start, a_start, b_start, c_start, u_start, v_start,
            //          x_end,   y_end,   z_end,   a_end,   b_end,   c_end,   u_end,   v_end,
            //          center_x_offset, center_y_offset, direction, angle);
            ARC_FEED(
                s.CurrentX, s.CurrentY, s.CurrentZ,
                s.AA_current, s.BB_current, s.CC_current,
                s.UU_current, s.VV_current,
                fe, se, /*z unchanged*/ s.CurrentZ,
                fa - s.CurrentX, sa - s.CurrentY,
                /*a/b/c/u/v unchanged*/ 0, 0, 0, 0, 0, 0,
                dir, ae
            );

            // update current position to arc end
            s.CurrentX = fe;
            s.CurrentY = se;
            // (leave Z,A,B,C,U,V unchanged for pure planar arcs)
            return RS274NGC_OK;
        }

        /// <summary>
        /// Compute the 2D arc parameters for G₂/G₃ in the active plane. :contentReference[oaicite:2]{index=2}</summary>
        private static int ArcData(
            int motion, Block b, SetupData s,
            out double fe, out double se,
            out double fa, out double sa,
            out int dir, out double ae)
        {
            // fe/se = end‐point in XY (or permuted)  
            // fa/sa = center‐point in XY (or permuted)  
            // dir   = CW/CCW flag  
            // ae    = sweep angle (0…2π)
            int status;
            switch (s.plane)
            {
                case CanonPlane.XY:
                    status = ArcDataCenter(
                        s.CurrentX, s.CurrentY,
                        b.XNumber, b.YNumber,
                        b.IFlag ? b.INumber : 0,
                        b.JFlag ? b.JNumber : 0,
                        out fe, out se, out fa, out sa,
                        out dir, out ae);
                    break;
                case CanonPlane.XZ:
                    status = ArcDataCenter(
                        s.CurrentX, s.CurrentZ,
                        b.XNumber, b.ZNumber,
                        b.IFlag ? b.INumber : 0,
                        b.KFlag ? b.KNumber : 0,
                        out fe, out se, out fa, out sa,
                        out dir, out ae);
                    break;
                case CanonPlane.YZ:
                    status = ArcDataCenter(
                        s.CurrentY, s.CurrentZ,
                        b.YNumber, b.ZNumber,
                        b.JFlag ? b.JNumber : 0,
                        b.KFlag ? b.KNumber : 0,
                        out fe, out se, out fa, out sa,
                        out dir, out ae);
                    break;
                default:
                    return NCE_PLANE_IS_NOT_XY_YZ_OR_XZ;
            }
            if (status != RS274NGC_OK) return status;

            // if G₃ (CCW), sweep = 2π - sweep computed for G₂
            if (motion == G_3)
                ae = (2.0 * Math.PI) - ae;

            return RS274NGC_OK;
        }

        /// <summary>
        /// Center‐based arc geometry: given start (x0,y0), end (x1,y1), and I/J or K offset,
        /// compute end‐point fe/se, center fa/sa, direction bit, and sweep angle ae. :contentReference[oaicite:3]{index=3}</summary>
        private static int ArcDataCenter(
            double x0, double y0,
            double x1, double y1,
            double iOffset, double jOffset,
            out double fe, out double se,
            out double fa, out double sa,
            out int dir, out double ae)
        {
            // center is start + offset
            fa = x0 + iOffset;
            sa = y0 + jOffset;

            // radius = distance center→start
            double r = Math.Sqrt((x0 - fa) * (x0 - fa) + (y0 - sa) * (y0 - sa));
            if (r < SIGMA) return NCE_ARC_RADIUS_TOO_SMALL;

            // compute start/end angles
            double theta0 = Math.Atan2(y0 - sa, x0 - fa);
            double theta1 = Math.Atan2(y1 - sa, x1 - fa);
            ae = theta1 - theta0;

            // normalize sweep to [0,2π)
            if (ae < 0) ae += 2.0 * Math.PI;
            if (ae >= 2.0 * Math.PI) ae -= 2.0 * Math.PI;

            // direction bit: 0 = CW, 1 = CCW (matches ARC_FEED API)
            dir = (ae > 0) ? 1 : 0;

            // fe/se are simply the end‐point coordinates
            fe = x1;
            se = y1;
            return RS274NGC_OK;
        }
        public class ErrorCode
        {
            private static ErrorCode ConvertCycleG84(
                   CANON_PLANE plane,
                   double x, double y,
                   double r,           // retract plane
                   double clearZ,      // clearance plane
                   double bottomZ,     // bottom of hole
                   CANON_DIRECTION direction,
                   CANON_SPEED_FEED_MODE mode)
            {
                // spindle must be turning
                if (direction != CanonDirection.Clockwise &&
                    direction != CanonDirection.CounterClockwise)
                    return ErrorCode.SpindleNotTurningInG84;

                // start synchronized speed/​feed if requested
                if (mode != CanonSpeedFeedMode.Synched)
                    StartSpeedFeedSynch();

                CycleFeed(plane, x, y, bottomZ);
                StopSpindleTurning();
                CycleTraverse(plane, x, y, clearZ);

                if (direction == CanonDirection.Clockwise)
                    StartSpindleClockwise();
                else
                    StartSpindleCounterclockwise();

                if (mode != CanonSpeedFeedMode.Synched)
                    StopSpeedFeedSynch();

                StopSpindleTurning();
                StartSpindleClockwise();

                return ErrorCode.Ok;
            }

            // convert_cycle_g85  — G85 (boring/​reaming) :contentReference[oaicite:11]{index=11}
            private static ErrorCode ConvertCycleG85(
                CANON_PLANE plane,
                double x, double y,
                double r,           // retract plane
                double clearZ,      // clearance plane
                double bottomZ)     // bottom of hole
            {
                CycleFeed(plane, x, y, bottomZ);
                CycleFeed(plane, x, y, r);
                CycleTraverse(plane, x, y, clearZ);
                return ErrorCode.Ok;
            }

            // convert_cycle_g86  — G86 (boring with dwell then retract and restart) :contentReference[oaicite:12]{index=12}
            private static ErrorCode ConvertCycleG86(
                CANON_PLANE plane,
                double x, double y,
                double clearZ,      // clearance plane
                double bottomZ,     // bottom of hole
                double dwell,       // dwell time
                CANON_DIRECTION direction)
            {
                if (direction != CanonDirection.Clockwise &&
                    direction != CanonDirection.CounterClockwise)
                    return ErrorCode.SpindleNotTurningInG86;

                CycleFeed(plane, x, y, bottomZ);
                Dwell(dwell);
                StopSpindleTurning();
                CycleTraverse(plane, x, y, clearZ);

                if (direction == CanonDirection.Clockwise)
                    StartSpindleClockwise();
                else
                    StartSpindleCounterclockwise();

                return ErrorCode.Ok;
            }

            // convert_cycle_g87  — G87 (back-boring) :contentReference[oaicite:13]{index=13}
            private static ErrorCode ConvertCycleG87(
                CANON_PLANE plane,
                double x, double offsetX,
                double y, double offsetY,
                double r,           // retract plane
                double clearZ,      // clearance plane
                double middleZ,
                double bottomZ,
                CANON_DIRECTION direction)
            {
                CycleTraverse(plane, offsetX, offsetY, r);
                StopSpindleTurning();
                OrientSpindle(0.0, direction);
                CycleTraverse(plane, offsetX, offsetY, bottomZ);
                CycleTraverse(plane, offsetX, offsetY, clearZ);
                CycleTraverse(plane, x, y, clearZ);

                if (direction == CanonDirection.Clockwise)
                    StartSpindleClockwise();
                else
                    StartSpindleCounterclockwise();

                return ErrorCode.Ok;
            }

            // convert_cycle_g88  — G88 (boring with program stop) :contentReference[oaicite:14]{index=14}
            private static ErrorCode ConvertCycleG88(
                CANON_PLANE plane,
                double x, double y,
                double bottomZ,     // bottom of hole
                double dwell,       // dwell time
                CANON_DIRECTION direction)
            {
                if (direction != CanonDirection.Clockwise &&
                    direction != CanonDirection.CounterClockwise)
                    return ErrorCode.SpindleNotTurningInG88;

                CycleFeed(plane, x, y, bottomZ);
                Dwell(dwell);
                StopSpindleTurning();
                ProgramStop();

                if (direction == CanonDirection.Clockwise)
                    StartSpindleClockwise();
                else
                    StartSpindleCounterclockwise();

                return ErrorCode.Ok;
            }

            // convert_cycle_g89  — G89 (boring with dwell then feed-retract) :contentReference[oaicite:15]{index=15}
            private static ErrorCode ConvertCycleG89(
                CANON_PLANE plane,
                double x, double y,
                double clearZ,      // clearance plane
                double bottomZ,     // bottom of hole
                double dwell)       // dwell time
            {
                CycleFeed(plane, x, y, bottomZ);
                Dwell(dwell);
                CycleFeed(plane, x, y, clearZ);
                return ErrorCode.Ok;
            }

            // === Plane‐specific wrappers ===

            // convert_cycle_yz  — dispatch G81–G89 in YZ plane :contentReference[oaicite:16]{index=16}
            private static ErrorCode ConvertCycleYZ(int motion, Block block, Setup settings)
            {
                // Resolve endpoints & depths exactly as in XY, but permuted for YZ...
                // (Identify old_cc, r, cc, clear_cc, aa, bb same as C++.)

                // Ensure exact-path for the cycle
                var saveMode = GetExternalMotionControlMode();
                if (saveMode != MotionControlMode.ExactPath)
                    SetMotionControlMode(MotionControlMode.ExactPath);

                ErrorCode status;
                switch (motion)
                {
                    case GCode.G81:
                        status = ConvertCycleG81(CanonPlane.YZ, aa, bb, clear_cc, cc);
                        break;
                    case GCode.G82:
                        status = ConvertCycleG82(CanonPlane.YZ, aa, bb, clear_cc, cc, block.P);
                        break;
                    case GCode.G83:
                        status = ConvertCycleG83(CanonPlane.YZ, aa, bb, r, clear_cc, cc, block.Q);
                        break;
                    case GCode.G84:
                        status = ConvertCycleG84(CanonPlane.YZ, aa, bb, r, clear_cc, cc, settings.SpindleTurning, settings.SpeedFeedMode);
                        break;
                    case GCode.G85:
                        status = ConvertCycleG85(CanonPlane.YZ, aa, bb, r, clear_cc, cc);
                        break;
                    case GCode.G86:
                        status = ConvertCycleG86(CanonPlane.YZ, aa, bb, clear_cc, cc, block.P, settings.SpindleTurning);
                        break;
                    case GCode.G87:
                        status = ConvertCycleG87(CanonPlane.YZ, aa, aa + block.J, bb, bb + block.K, r, clear_cc, block.I, cc, settings.SpindleTurning);
                        break;
                    case GCode.G88:
                        status = ConvertCycleG88(CanonPlane.YZ, aa, bb, cc, block.P, settings.SpindleTurning);
                        break;
                    case GCode.G89:
                        status = ConvertCycleG89(CanonPlane.YZ, aa, bb, clear_cc, cc, block.P);
                        break;
                    default:
                        return ErrorCode.FunctionShouldNotHaveBeenCalled;
                }

                // Restore motion-control mode
                if (saveMode != MotionControlMode.ExactPath)
                    SetMotionControlMode(saveMode);

                return status;
            }


            // convert_cycle_zx  — dispatch G81–G89 in XZ plane :contentReference[oaicite:17]{index=17}
            private static ErrorCode ConvertCycleZX(int motion, Block block, Setup settings)
            {
                // Resolve endpoints & depths permuted for XZ...

                var saveMode = GetExternalMotionControlMode();
                if (saveMode != MotionControlMode.ExactPath)
                    SetMotionControlMode(MotionControlMode.ExactPath);

                ErrorCode status;
                switch (motion)
                {
                    case GCode.G81:
                        status = ConvertCycleG81(CanonPlane.XZ, aa, bb, clear_cc, cc);
                        break;
                    case GCode.G82:
                        status = ConvertCycleG82(CanonPlane.XZ, aa, bb, clear_cc, cc, block.P);
                        break;
                    case GCode.G83:
                        status = ConvertCycleG83(CanonPlane.XZ, aa, bb, r, clear_cc, cc, block.Q);
                        break;
                    case GCode.G84:
                        status = ConvertCycleG84(CanonPlane.XZ, aa, bb, r, clear_cc, cc, settings.SpindleTurning, settings.SpeedFeedMode);
                        break;
                    case GCode.G85:
                        status = ConvertCycleG85(CanonPlane.XZ, aa, bb, r, clear_cc, cc);
                        break;
                    case GCode.G86:
                        status = ConvertCycleG86(CanonPlane.XZ, aa, bb, clear_cc, cc, block.P, settings.SpindleTurning);
                        break;
                    case GCode.G87:
                        status = ConvertCycleG87(CanonPlane.XZ, aa, aa + block.K, bb, bb + block.I, r, clear_cc, block.J, cc, settings.SpindleTurning);
                        break;
                    case GCode.G88:
                        status = ConvertCycleG88(CanonPlane.XZ, aa, bb, cc, block.P, settings.SpindleTurning);
                        break;
                    case GCode.G89:
                        status = ConvertCycleG89(CanonPlane.XZ, aa, bb, clear_cc, cc, block.P);
                        break;
                    default:
                        return ErrorCode.FunctionShouldNotHaveBeenCalled;
                }

                if (saveMode != MotionControlMode.ExactPath)
                    SetMotionControlMode(saveMode);

                return status;
            }
            private static ErrorCode ConvertThread(int move, Block block, Setup settings)
            {
                if (settings.FeedRate == 0.0)
                    return ErrorCode.CannotDoG32WithZeroFeedRate;
                if (settings.CutterCompSide != CutterCompSide.Off)
                    return ErrorCode.CannotUseG32WithCutterRadiusComp;

                settings.MotionMode = move;

                // Compute absolute end-point (x,y,z,AA,BB,CC,UU,VV)
                FindEnds(block, settings,
                        out double endX, out double endY, out double endZ,
                        out double aaEnd, out double bbEnd, out double ccEnd,
                        out double uuEnd, out double vvEnd);

                StraightFeed(endX, endY, endZ, aaEnd, bbEnd, ccEnd, uuEnd, vvEnd);

                settings.CurrentX = endX;
                settings.CurrentY = endY;
                settings.CurrentZ = endZ;
                settings.AACurrent = aaEnd;
                settings.BBCurrent = bbEnd;
                settings.CCCurrent = ccEnd;
                settings.UUCurrent = uuEnd;
                settings.VVCurrent = vvEnd;

                return ErrorCode.Ok;
            }
        }
    }
    
}