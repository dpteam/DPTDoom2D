//
// DPT.DOOM 2D — actors.txt: the actor description language.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// One actor block from actors.txt.
    ///
    /// The format is a deliberately small cousin of ZDoom's DECORATE: the same
    /// idea (a named actor, properties, labelled states), none of the machinery
    /// that a 1996 2D game has no use for. There are no expressions, no ACS, no
    /// actions and no jumps — the engine's monster AI stays in monster.c, and a
    /// definition only supplies the data that AI reads: sizes, sprite frames,
    /// sounds and the frame sequences of each state.
    /// </summary>
    public sealed class ActorDef
    {
        public string Name;
        public string Source;
        public int Line;

        /// <summary>Numbers: radius, height, health, ... See <see cref="ActorParser.Properties"/>.</summary>
        public readonly Dictionary<string, int> Props = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Sprite set name, upper case. Empty when the actor is drawn by nothing —
        /// "none", "NULL", "----" and "TNT1" all mean "simulate it, draw nothing",
        /// which is how ZDoom spells an invisible actor.
        /// </summary>
        public string Sprite = "";

        /// <summary>
        /// True when the definition named a sprite at all. An override that only
        /// changes health must leave the sprite alone, so "no sprite line" and
        /// "sprite none" cannot both be an empty string.
        /// </summary>
        public bool SpriteSet;

        /// <summary>Sound lump names without the DS prefix, by slot (see, pain, attack, idle, death).</summary>
        public readonly Dictionary<string, string> Sounds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Compiled frame strings, by state label (spawn, see, pain, ...).</summary>
        public readonly Dictionary<string, string> States = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where a value came from, for the log: "actors.txt:12".</summary>
        public string Where
        {
            get { return Source + ":" + Line; }
        }
    }

    /// <summary>
    /// Lexer and parser for actors.txt.
    ///
    /// A broken file never throws: every problem becomes a line in
    /// <c>errors</c> shaped <c>source:line: code: message</c>, which is what
    /// --modlint prints and what an agent can parse. The caller decides whether
    /// the actor keeps its defaults.
    /// </summary>
    public static class ActorParser
    {
        /// <summary>
        /// State labels the engine knows. The order must match Actors.Lbl*: the first
        /// nine are the save file's numbering, missile comes last because it has no
        /// slot there.
        /// </summary>
        public static readonly string[] Labels =
        {
            "spawn", "see", "pain", "wait", "melee", "death", "slop", "dead", "mess", "missile"
        };

        /// <summary>Sound slots, in the order monster.c keeps them.</summary>
        public static readonly string[] SoundSlots = { "see", "pain", "attack", "idle", "death" };

        /// <summary>Numeric properties and the range they are clamped to.</summary>
        public static readonly string[] Properties =
        {
            "radius", "height", "health", "painchance", "revisit", "jump", "slop", "minpain",
            "sprite", "frames", "renderstyle", "translation"
        };

        /// <summary>Render styles. The 8-bit renderer can stipple and it can skip; it has no additive blending.</summary>
        public static readonly string[] RenderStyles = { "normal", "invisible", "fuzz" };

        /// <summary>Sprite names that mean "no picture": ZDoom's NULL and TNT1, and the old "----" marker.</summary>
        static readonly string[] NoSprite = { "NONE", "NULL", "----", "TNT1", "TNT2" };

        const int MaxTicks = 120;

        enum Kind { Word, Number, Text, Sym, End }

        struct Token
        {
            public Kind Kind;
            public string Text;
            public int Value;
            public int Line;
        }

        public static List<ActorDef> Parse(string text, string source, List<string> errors)
        {
            var tokens = Lex(text, source, errors);
            var actors = new List<ActorDef>();
            int at = 0;

            while (at < tokens.Count && tokens[at].Kind != Kind.End)
            {
                Token token = tokens[at];

                if (token.Kind != Kind.Word || !Eq(token.Text, "actor"))
                {
                    Error(errors, source, token.Line, "E104", "expected 'actor', found '" + Show(token) + "'");
                    at = SkipActor(tokens, at);
                    continue;
                }

                ActorDef def = ParseActor(tokens, ref at, source, errors);
                if (def != null) actors.Add(def);
            }

            return actors;
        }

        /// <summary>Parses one file and returns the actors it defines, logging any errors.</summary>
        public static List<ActorDef> ParseFile(string text, string source)
        {
            var errors = new List<string>();
            List<ActorDef> actors = Parse(text, source, errors);

            for (int i = 0; i < errors.Count; ++i) Log.Warn("actors: " + errors[i]);
            if (errors.Count > 0)
                Log.Warn("actors: " + errors.Count + " problem(s) in " + source);

            return actors;
        }

        // ------------------------------------------------------------------ actor

        static ActorDef ParseActor(List<Token> tokens, ref int at, string source, List<string> errors)
        {
            var def = new ActorDef();
            def.Source = source;
            def.Line = tokens[at].Line;
            ++at;   // "actor"

            if (at >= tokens.Count || tokens[at].Kind != Kind.Word)
            {
                Error(errors, source, def.Line, "E104", "actor needs a name");
                at = SkipActor(tokens, at);
                return null;
            }

            def.Name = tokens[at].Text;
            ++at;

            if (at < tokens.Count && tokens[at].Kind == Kind.Sym && tokens[at].Text == ":")
            {
                // Inherited actors are accepted by the grammar so that a Doom
                // DECORATE file ported by hand does not have to be rewritten, but
                // stage 1 has nothing to inherit from: every actor overrides a
                // built-in type by name.
                Error(errors, source, tokens[at].Line, "E105",
                    "inheritance ('" + def.Name + " : ...') is not supported yet; name a built-in actor instead");
                ++at;
                if (at < tokens.Count && tokens[at].Kind == Kind.Word) ++at;
            }

            if (!ExpectSym(tokens, ref at, "{", source, errors)) return def;

            while (at < tokens.Count)
            {
                Token token = tokens[at];

                if (token.Kind == Kind.Sym && token.Text == "}") { ++at; return def; }
                if (token.Kind == Kind.End)
                {
                    Error(errors, source, token.Line, "E104", "unexpected end of file inside '" + def.Name + "'");
                    return def;
                }

                if (token.Kind != Kind.Word)
                {
                    Error(errors, source, token.Line, "E104", "expected a property, found '" + Show(token) + "'");
                    ++at;
                    continue;
                }

                if (Eq(token.Text, "states"))
                {
                    ++at;
                    ParseStates(tokens, ref at, def, source, errors);
                    continue;
                }

                if (Eq(token.Text, "sound"))
                {
                    ++at;
                    ParseSound(tokens, ref at, def, source, errors);
                    continue;
                }

                ParseProperty(tokens, ref at, def, source, errors);
            }

            Error(errors, source, def.Line, "E104", "unexpected end of file, '" + def.Name + "' is not closed");
            return def;
        }

        static void ParseProperty(List<Token> tokens, ref int at, ActorDef def, string source, List<string> errors)
        {
            Token name = tokens[at];
            ++at;

            string key = name.Text.ToLowerInvariant();
            bool known = false;
            for (int i = 0; i < Properties.Length; ++i)
                if (Properties[i] == key) { known = true; break; }

            if (!known)
            {
                Error(errors, source, name.Line, "E101",
                    "unknown property '" + name.Text + "' (known: " + string.Join(", ", Properties) + ")");
                SkipValue(tokens, ref at);
                return;
            }

            if (at >= tokens.Count || tokens[at].Kind == Kind.Sym)
            {
                Error(errors, source, name.Line, "E103", "'" + key + "' needs a value");
                return;
            }

            Token value = tokens[at];
            ++at;

            if (Eq(key, "sprite"))
            {
                if (value.Kind != Kind.Word && value.Kind != Kind.Text)
                {
                    Error(errors, source, value.Line, "E103", "'sprite' needs a name, found '" + Show(value) + "'");
                    return;
                }

                string sprite = value.Text.ToUpperInvariant();

                // "none", "NULL", "----", "TNT1": the actor exists, collides and is
                // simulated, but nothing is drawn. A mod uses this for a trigger
                // helper or a spectre-like monster.
                if (IsNoSprite(sprite))
                {
                    def.Sprite = "";
                    def.SpriteSet = true;
                    return;
                }

                // WAD sprite names are four characters; a shorter one is padded by
                // the resource lookup, so "FISH" and "FIS" would collide.
                if (sprite.Length == 0 || sprite.Length > 4)
                {
                    Error(errors, source, value.Line, "E103",
                        "sprite name must be 1..4 characters, got '" + value.Text + "'");
                    return;
                }

                def.Sprite = sprite;
                def.SpriteSet = true;
                return;
            }

            if (Eq(key, "renderstyle"))
            {
                if (value.Kind != Kind.Word && value.Kind != Kind.Text)
                {
                    Error(errors, source, value.Line, "E103",
                        "'renderstyle' needs a name: " + string.Join(", ", RenderStyles));
                    return;
                }

                string style = value.Text.ToLowerInvariant();
                int styleIndex = Array.IndexOf(RenderStyles, style);

                if (styleIndex < 0)
                {
                    // ZDoom's "add", "subtract", "translucent", "stencil" and
                    // "shadow" all need alpha blending, which an 8-bit palette
                    // renderer does not have. Say so instead of pretending.
                    Error(errors, source, value.Line, "E106",
                        "unknown renderstyle '" + value.Text + "' (this renderer supports: "
                        + string.Join(", ", RenderStyles) + "; additive styles need a true-colour renderer)");
                    return;
                }

                def.Props["renderstyle"] = styleIndex;
                return;
            }

            if (value.Kind != Kind.Number)
            {
                Error(errors, source, value.Line, "E103", "'" + key + "' needs a number, found '" + Show(value) + "'");
                return;
            }

            int number = value.Value;

            if (Eq(key, "frames"))
            {
                if (number < 0 || number > 58)
                {
                    Error(errors, source, value.Line, "E103", "'frames' must be 0..58, got " + number);
                    return;
                }

                def.Props["frames"] = number;
                return;
            }

            if (number < 0)
            {
                Error(errors, source, value.Line, "E103", "'" + key + "' cannot be negative");
                return;
            }

            def.Props[key] = number;
        }

        static void ParseSound(List<Token> tokens, ref int at, ActorDef def, string source, List<string> errors)
        {
            if (at >= tokens.Count || tokens[at].Kind != Kind.Word)
            {
                Error(errors, source, at < tokens.Count ? tokens[at].Line : def.Line, "E104", "'sound' needs a slot");
                return;
            }

            Token slot = tokens[at];
            ++at;

            string name = slot.Text.ToLowerInvariant();
            bool known = false;
            for (int i = 0; i < SoundSlots.Length; ++i)
                if (SoundSlots[i] == name) { known = true; break; }

            if (!known)
            {
                Error(errors, source, slot.Line, "E106",
                    "unknown sound slot '" + slot.Text + "' (known: " + string.Join(", ", SoundSlots) + ")");
                SkipValue(tokens, ref at);
                return;
            }

            if (at >= tokens.Count || tokens[at].Kind == Kind.Sym)
            {
                Error(errors, source, slot.Line, "E103", "'sound " + name + "' needs a lump name");
                return;
            }

            Token value = tokens[at];
            ++at;

            // The lump is DS<name>; "none" means "keep the slot silent", which is
            // how most monsters have no idle sound.
            if (Eq(value.Text, "none"))
            {
                def.Sounds[name] = "";
                return;
            }

            def.Sounds[name] = value.Text.ToUpperInvariant();
        }

        static void ParseStates(List<Token> tokens, ref int at, ActorDef def, string source, List<string> errors)
        {
            if (!ExpectSym(tokens, ref at, "{", source, errors)) return;

            while (at < tokens.Count)
            {
                Token token = tokens[at];

                if (token.Kind == Kind.Sym && token.Text == "}") { ++at; return; }
                if (token.Kind == Kind.End)
                {
                    Error(errors, source, token.Line, "E104", "unexpected end of file in 'states'");
                    return;
                }

                if (token.Kind != Kind.Word || at + 1 >= tokens.Count
                    || tokens[at + 1].Kind != Kind.Sym || tokens[at + 1].Text != ":")
                {
                    Error(errors, source, token.Line, "E104",
                        "expected a state label like 'see:', found '" + Show(token) + "'");
                    ++at;
                    continue;
                }

                string label = token.Text.ToLowerInvariant();
                int labelLine = token.Line;
                at += 2;

                bool known = false;
                for (int i = 0; i < Labels.Length; ++i)
                    if (Labels[i] == label) { known = true; break; }

                if (!known)
                {
                    Error(errors, source, labelLine, "E102",
                        "unknown state '" + label + "' (known: " + string.Join(", ", Labels) + ")");
                    SkipFrames(tokens, ref at);
                    continue;
                }

                bool explicitNone = at < tokens.Count && tokens[at].Kind == Kind.Word
                    && Eq(tokens[at].Text, "none");

                string frames = ParseFrames(tokens, ref at, label, source, errors);
                if (frames.Length == 0 && !explicitNone)
                {
                    Error(errors, source, labelLine, "W201", "state '" + label + "' is empty");
                    continue;
                }

                def.States[label] = frames;
            }
        }

        static string ParseFrames(List<Token> tokens, ref int at, string label, string source, List<string> errors)
        {
            var text = new StringBuilder();

            while (at < tokens.Count)
            {
                Token token = tokens[at];

                if (token.Kind == Kind.Sym) break;   // '}' or the next label's ':'

                // "see:" — the next state label, not four frames spelled s, e, e.
                if (token.Kind == Kind.Word && at + 1 < tokens.Count
                    && tokens[at + 1].Kind == Kind.Sym && tokens[at + 1].Text == ":")
                    break;

                if (token.Kind == Kind.Word && Eq(token.Text, "none"))
                {
                    // An explicitly empty state: the monster keeps whatever frame it
                    // was showing, which is what monster.c's "" entries do.
                    ++at;
                    return text.ToString();
                }

                if (token.Kind == Kind.Word && IsUnsupported(token.Text))
                {
                    Error(errors, source, token.Line, "E105",
                        "'" + token.Text + "' is not supported yet: the engine's state machine is still in monster.c");
                    ++at;
                    continue;
                }

                if (token.Kind != Kind.Word && token.Kind != Kind.Text)
                {
                    Error(errors, source, token.Line, "E104",
                        "expected frame letters, found '" + Show(token) + "'");
                    ++at;
                    continue;
                }

                string letters = token.Text;
                ++at;

                int ticks = 1;
                if (at < tokens.Count && tokens[at].Kind == Kind.Number)
                {
                    ticks = tokens[at].Value;
                    if (ticks < 1 || ticks > MaxTicks)
                    {
                        Error(errors, source, tokens[at].Line, "E103",
                            "frame duration must be 1.." + MaxTicks + " ticks, got " + ticks);
                        ticks = 1;
                    }
                    ++at;
                }

                for (int i = 0; i < letters.Length; ++i)
                {
                    char c = letters[i];
                    if (!IsFrameChar(c))
                    {
                        Error(errors, source, token.Line, "E109",
                            "frame letter '" + c + "' is out of range: use A..Z, or quote the sequence");
                        continue;
                    }

                    // Durations compile into repeated letters: the engine advances
                    // one frame per tick, so "A 4" and "AAAA" are the same thing and
                    // nothing in monster.c has to learn about timing.
                    for (int t = 0; t < ticks; ++t) text.Append(c);
                }

                if (text.Length > 512)
                {
                    Error(errors, source, token.Line, "E103", "state '" + label + "' is longer than 512 frames");
                    break;
                }
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------ lexer

        static List<Token> Lex(string text, string source, List<string> errors)
        {
            var tokens = new List<Token>();
            int i = 0, line = 1;

            while (i < text.Length)
            {
                char c = text[i];

                if (c == '\n') { ++line; ++i; continue; }
                if (c == '\r' || c == ' ' || c == '\t' || c == '\f' || c == '\v') { ++i; continue; }

                // Comments: '//' to end of line, and ';' as the 1996 config files
                // used it, so a Doom DECORATE pasted next to a cfg still reads.
                if (c == ';' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    while (i < text.Length && text[i] != '\n') ++i;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int startLine = line;
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        if (text[i] == '\n') ++line;
                        ++i;
                    }
                    if (i + 1 >= text.Length)
                    {
                        Error(errors, source, startLine, "E104", "unterminated /* comment");
                        break;
                    }
                    i += 2;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    int startLine = line;
                    var value = new StringBuilder();
                    ++i;
                    bool closed = false;

                    while (i < text.Length)
                    {
                        char d = text[i];
                        if (d == quote) { ++i; closed = true; break; }
                        if (d == '\n') break;
                        if (d == '\\' && i + 1 < text.Length)
                        {
                            char e = text[i + 1];
                            if (e == '\\' || e == '"' || e == '\'') { value.Append(e); i += 2; continue; }
                        }
                        value.Append(d);
                        ++i;
                    }

                    if (!closed)
                        Error(errors, source, startLine, "E104", "unterminated string");

                    tokens.Add(new Token { Kind = Kind.Text, Text = value.ToString(), Line = startLine });
                    continue;
                }

                if (c >= '0' && c <= '9')
                {
                    int start = i;

                    // 0xD0 is how a palette translation is written: the same
                    // notation monster.c and the config files use.
                    if (c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
                    {
                        i += 2;
                        while (i < text.Length && IsHex(text[i])) ++i;

                        int hex;
                        if (!int.TryParse(text.Substring(start + 2, i - start - 2),
                                System.Globalization.NumberStyles.HexNumber, null, out hex))
                            hex = 0;

                        tokens.Add(new Token { Kind = Kind.Number, Text = text.Substring(start, i - start), Value = hex, Line = line });
                        continue;
                    }

                    while (i < text.Length && text[i] >= '0' && text[i] <= '9') ++i;

                    int value;
                    if (!int.TryParse(text.Substring(start, i - start), out value)) value = 0;
                    tokens.Add(new Token { Kind = Kind.Number, Text = text.Substring(start, i - start), Value = value, Line = line });
                    continue;
                }

                if (IsWordChar(c))
                {
                    int start = i;
                    while (i < text.Length && IsWordChar(text[i])) ++i;
                    tokens.Add(new Token { Kind = Kind.Word, Text = text.Substring(start, i - start), Line = line });
                    continue;
                }

                if (c == '{' || c == '}' || c == ':' || c == '=' || c == ',')
                {
                    tokens.Add(new Token { Kind = Kind.Sym, Text = c.ToString(), Line = line });
                    ++i;
                    continue;
                }

                Error(errors, source, line, "E104", "unexpected character '" + c + "'");
                ++i;
            }

            tokens.Add(new Token { Kind = Kind.End, Text = "", Line = line });
            return tokens;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Frame characters are packed as (letter - 'A') * 2 + direction by the
        /// renderer, so the range is 0..28: A..Z for sprites, then '[', '\' and ']'
        /// for the archvile's fire — exactly the characters monster.c spells.
        /// </summary>
        static bool IsFrameChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || c == '[' || c == '\\' || c == ']';
        }

        static bool IsUnsupported(string word)
        {
            return Eq(word, "stop") || Eq(word, "loop") || Eq(word, "goto");
        }

        /// <summary>True for the sprite names that mean "draw nothing".</summary>
        public static bool IsNoSprite(string name)
        {
            for (int i = 0; i < NoSprite.Length; ++i)
                if (string.Equals(name, NoSprite[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsWordChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-';
        }

        static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        static bool ExpectSym(List<Token> tokens, ref int at, string symbol, string source, List<string> errors)
        {
            if (at < tokens.Count && tokens[at].Kind == Kind.Sym && tokens[at].Text == symbol)
            {
                ++at;
                return true;
            }

            int line = at < tokens.Count ? tokens[at].Line : 0;
            Error(errors, source, line, "E104",
                "expected '" + symbol + "', found '" + (at < tokens.Count ? Show(tokens[at]) : "end of file") + "'");
            return false;
        }

        /// <summary>Skips a property value that could not be understood.</summary>
        static void SkipValue(List<Token> tokens, ref int at)
        {
            if (at < tokens.Count && tokens[at].Kind != Kind.Sym) ++at;
        }

        /// <summary>Skips the rest of a state whose label is unknown.</summary>
        static void SkipFrames(List<Token> tokens, ref int at)
        {
            while (at < tokens.Count && tokens[at].Kind != Kind.Sym) ++at;
        }

        /// <summary>Recovery: jump to the next 'actor' word so one typo does not eat the file.</summary>
        static int SkipActor(List<Token> tokens, int at)
        {
            while (at < tokens.Count && tokens[at].Kind != Kind.End)
            {
                if (tokens[at].Kind == Kind.Word && Eq(tokens[at].Text, "actor")) return at;
                ++at;
            }
            return at;
        }

        static string Show(Token token)
        {
            if (token.Kind == Kind.End) return "end of file";
            if (token.Kind == Kind.Text) return "\"" + token.Text + "\"";
            return token.Text;
        }

        static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

                static void Error(List<string> errors, string source, int line, string code, string message)
        {
            errors.Add(source + ":" + line + ": " + code + ": " + message);
        }
    }
}
