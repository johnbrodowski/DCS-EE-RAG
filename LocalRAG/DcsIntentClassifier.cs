using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LocalRAG
{
    /// <summary>
    /// Pattern-based classifier for assigning intent and domain identifiers to messages.
    /// Supports multi-intent detection: a single message can match multiple intents.
    /// </summary>
    public static class DcsIntentClassifier
    {
        // ── Intent IDs ──────────────────────────────────────────────────────

        public const int INTENT_QUERY = 1;
        public const int INTENT_INSTRUCTION = 2;
        public const int INTENT_IMPLEMENTATION = 3;
        public const int INTENT_CLARIFICATION = 4;
        public const int INTENT_CORRECTION = 5;
        public const int INTENT_CHAT = 6;
        public const int INTENT_DESIGN = 7;
        public const int INTENT_FIX = 8;

        // ── Domain IDs ──────────────────────────────────────────────────────

        public const int DOMAIN_BACKEND = 1;
        public const int DOMAIN_FRONTEND = 2;
        public const int DOMAIN_INFRASTRUCTURE = 3;
        public const int DOMAIN_RESEARCH = 4;

        // ── Intent patterns (checked in order, multiple can match) ─────────

        private static readonly (int IntentId, Regex Pattern)[] IntentPatterns =
        [
            (INTENT_QUERY,          new Regex(@"\b(what|how|why|when|where|which|who|explain|describe|tell me|show me)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_INSTRUCTION,    new Regex(@"\b(create|build|add|make|set up|configure|implement|write|generate)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_IMPLEMENTATION, new Regex(@"\b(code|function|class|method|module|component|service|endpoint|api)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_CLARIFICATION,  new Regex(@"\b(clarify|mean|meaning|confused|unclear|understand|difference between)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_CORRECTION,     new Regex(@"\b(wrong|incorrect|mistake|error|fix this|that's not|actually|instead)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_DESIGN,         new Regex(@"\b(design|architect|pattern|structure|approach|strategy|plan|diagram)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_FIX,            new Regex(@"\b(fix|bug|broken|crash|fail|issue|problem|debug|troubleshoot|resolve)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (INTENT_CHAT,           new Regex(@"\b(hello|hi|hey|thanks|thank you|okay|ok|sure|great|nice|cool)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ];

        // ── Domain patterns ────────────────────────────────────────────────

        private static readonly (int DomainId, Regex Pattern)[] DomainPatterns =
        [
            (DOMAIN_BACKEND,        new Regex(@"\b(backend|server|api|database|sql|endpoint|controller|service|repository|migration|queue|cache)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (DOMAIN_FRONTEND,       new Regex(@"\b(frontend|ui|ux|component|page|view|form|button|css|html|react|angular|vue|blazor|javascript|typescript)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (DOMAIN_INFRASTRUCTURE, new Regex(@"\b(infrastructure|deploy|ci|cd|pipeline|docker|kubernetes|k8s|terraform|cloud|aws|azure|gcp|nginx|config|environment)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            (DOMAIN_RESEARCH,       new Regex(@"\b(research|paper|study|experiment|benchmark|evaluate|compare|analysis|hypothesis|finding|literature)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ];

        /// <summary>
        /// Classifies intents from message text. Returns all matching intent IDs.
        /// Falls back to INTENT_CHAT if no patterns match.
        /// </summary>
        public static int[] ClassifyIntents(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Classify > Intents: [CHAT(6)] (empty message)");
                return [INTENT_CHAT];
            }

            var matched = IntentPatterns
                .Where(p => p.Pattern.IsMatch(message))
                .Select(p => p.IntentId)
                .Distinct()
                .ToArray();

            var result = matched.Length > 0 ? matched : [INTENT_CHAT];
            var names = string.Join(", ", result.Select(id => $"{FormatIntentName(id)}({id})"));
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Classify > Intents: [{names}] from \"{Truncate(message, 80)}\"");
            return result;
        }

        /// <summary>
        /// Classifies domains from message text. Returns all matching domain IDs.
        /// Returns empty array if no domain patterns match (domain is a secondary signal).
        /// </summary>
        public static int[] ClassifyDomains(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Classify > Domains: [] (empty message)");
                return Array.Empty<int>();
            }

            var result = DomainPatterns
                .Where(p => p.Pattern.IsMatch(message))
                .Select(p => p.DomainId)
                .Distinct()
                .ToArray();

            var names = result.Length > 0
                ? string.Join(", ", result.Select(id => $"{FormatDomainName(id)}({id})"))
                : "none";
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Classify > Domains: [{names}] from \"{Truncate(message, 80)}\"");
            return result;
        }

        /// <summary>
        /// Builds a complete DcsKey by classifying both intents and domains.
        /// DCN IDs are not assigned here — they come from the context assembler.
        /// </summary>
        public static DcsKey Classify(string message)
        {
            var key = new DcsKey
            {
                IntentIds = ClassifyIntents(message),
                DomainIds = ClassifyDomains(message)
            };
            Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Classify > Key: intents=[{string.Join(",", key.IntentIds)}] domains=[{string.Join(",", key.DomainIds)}] dcns=[] for \"{Truncate(message, 80)}\"");
            return key;
        }

        /// <summary>
        /// Determines whether a transition from one set of intents to another
        /// represents an escalation (e.g. CHAT → DESIGN or CHAT → FIX).
        /// Used in behavioral influence scoring.
        /// </summary>
        public static bool IsEscalation(int[] fromIntents, int[] toIntents)
        {
            var lowIntents = new HashSet<int> { INTENT_CHAT, INTENT_QUERY, INTENT_CLARIFICATION };
            var highIntents = new HashSet<int> { INTENT_DESIGN, INTENT_FIX, INTENT_IMPLEMENTATION, INTENT_INSTRUCTION };

            bool fromIsLow = fromIntents.Any(i => lowIntents.Contains(i));
            bool toIsHigh = toIntents.Any(i => highIntents.Contains(i));

            bool result = fromIsLow && toIsHigh;
            if (result)
            {
                Debug.WriteLine($"[DCS {DateTime.Now:HH:mm:ss.fff}] Classify > Escalation detected: [{string.Join(",", fromIntents)}] -> [{string.Join(",", toIntents)}]");
            }
            return result;
        }

        // ── Logging helpers ──────────────────────────────────────────────────

        private static string FormatIntentName(int id) => id switch
        {
            INTENT_QUERY => "QUERY",
            INTENT_INSTRUCTION => "INSTRUCTION",
            INTENT_IMPLEMENTATION => "IMPLEMENTATION",
            INTENT_CLARIFICATION => "CLARIFICATION",
            INTENT_CORRECTION => "CORRECTION",
            INTENT_CHAT => "CHAT",
            INTENT_DESIGN => "DESIGN",
            INTENT_FIX => "FIX",
            _ => $"UNKNOWN_{id}"
        };

        private static string FormatDomainName(int id) => id switch
        {
            DOMAIN_BACKEND => "BACKEND",
            DOMAIN_FRONTEND => "FRONTEND",
            DOMAIN_INFRASTRUCTURE => "INFRASTRUCTURE",
            DOMAIN_RESEARCH => "RESEARCH",
            _ => $"UNKNOWN_{id}"
        };

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s[..max] + "...";
        }
    }
}
