using System;
using System.Collections.Generic;

namespace LangFixer
{
    /// <summary>
    /// Tie-breaker for cross-layout typo repair. The Windows Hebrew checker accepts many prefixed or rare
    /// forms (for בידקה it accepts both בדיקה and יבדקה), so when several repaired candidates are "valid"
    /// the one on this everyday list wins. Not a dictionary: only a few hundred common words.
    /// </summary>
    internal static class CommonWords
    {
        /// <summary>
        /// Everyday English. A word typed on the Hebrew layout is converted to English only if the English
        /// rendering is on this list or at least 6 letters long: the Windows English dictionary accepts obscure
        /// entries such as "hyuk", which turned the Hebrew slip יטולת into "hyuk,".
        /// </summary>
        public static readonly HashSet<string> English = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "it's","don't","doesn't","didn't","isn't","aren't","wasn't","weren't","won't","wouldn't","couldn't","shouldn't",
            "can't","hasn't","haven't","hadn't","i'm","i've","i'll","i'd","you're","you've","you'll","you'd","we're","we've",
            "we'll","they're","they've","they'll","he's","she's","that's","there's","what's","who's","let's","here's",
            "a","i","the","and","or","but","if","so","to","of","in","on","at","by","for","from","with","without","about",
            "into","onto","over","under","up","down","out","off","as","than","then","that","this","these","those","it",
            "its","is","are","was","were","be","been","being","am","do","does","did","done","have","has","had","having",
            "will","would","can","could","should","shall","may","might","must","need","needs","want","wants","wanted",
            "you","your","yours","he","she","we","they","me","him","her","us","them","my","our","their","his","who",
            "whom","whose","what","which","when","where","why","how","yes","no","not","never","always","often","also",
            "too","very","just","only","even","still","yet","again","back","here","there","now","today","tomorrow",
            "yesterday","soon","later","early","late","before","after","during","while","until","since","ago","once",
            "twice","first","last","next","new","old","good","bad","better","best","worse","worst","big","small","long",
            "short","high","low","more","less","most","least","much","many","few","some","any","all","each","every",
            "other","another","same","different","own","such","both","either","neither","one","two","three","four",
            "five","six","seven","eight","nine","ten","hundred","half","time","times","day","days","week","weeks",
            "month","months","year","years","hour","hours","minute","minutes","second","seconds","morning","night",
            "hello","hi","hey","bye","thanks","thank","please","sorry","welcome","sure","okay","fine","great","nice",
            "cool","wow","oh","yeah","yep","nope","maybe","right","wrong","true","false","really","exactly","almost",
            "enough","ready","done","wait","stop","start","go","come","get","got","give","take","make","made","let",
            "put","set","see","saw","seen","look","watch","show","tell","told","say","said","ask","asked","talk",
            "speak","call","called","text","write","wrote","read","send","sent","reply","answer","know","knew","think",
            "thought","mean","meant","feel","felt","find","found","try","tried","use","used","work","works","worked",
            "working","play","run","runs","ran","running","move","keep","kept","hold","turn","open","close","closed",
            "check","checked","fix","fixed","test","tested","build","built","change","changed","add","added","remove",
            "delete","update","updated","create","created","save","saved","load","copy","paste","cut","search","print",
            "help","learn","teach","study","buy","sell","pay","paid","cost","free","cheap","order","book","booked",
            "flight","flights","hotel","hotels","room","rooms","trip","travel","ticket","tickets","price","prices",
            "deal","offer","sale","client","clients","customer","customers","user","users","admin","team","manager",
            "boss","office","home","house","car","road","city","country","world","place","way","thing","things",
            "stuff","part","side","end","point","case","fact","idea","plan","problem","problems","issue","issues",
            "bug","bugs","error","errors","log","logs","data","file","files","page","pages","site","link","links",
            "app","apps","code","server","servers","service","system","version","release","branch","merge","commit",
            "push","pull","deploy","review","task","tasks","note","notes","list","table","query","cache","config",
            "login","email","mail","phone","message","messages","chat","meeting","meetings","report","reports","doc",
            "docs","image","video","photo","music","game","news","story","word","words","name","names","number",
            "numbers","line","lines","screen","window","button","click","type","typed","typing","key","keys","mouse",
            "keyboard","layout","language","english","hebrew","people","person","man","woman","child","kids","family",
            "friend","friends","father","mother","dad","mom","boy","girl","guy","guys","life","love","hate","like",
            "liked","hope","wish","sleep","eat","drink","food","water","coffee","tea","lunch","dinner","money","job",
            "work","school","class","test","question","questions","answer","answers","reason","because","why","yes",
            "ok","okay","hmm","um","lol","btw","fyi","asap","please","again","already","actually","probably","maybe",
            "definitely","obviously","basically","anyway","however","though","although","instead","otherwise",
            "together","alone","away","around","across","along","behind","between","inside","outside","near","far",
            "left","right","top","bottom","front","middle","center","above","below","against","toward","through",
            "little","lot","lots","bit","few","several","various","whole","full","empty","ready","busy","quick","slow",
            "fast","easy","hard","simple","clear","sure","safe","happy","sad","sick","tired","hungry","cold","hot",
            "warm","dark","light","black","white","red","blue","green","yellow","gray","color","colors","strong",
            "weak","heavy","young","real","main","local","public","private","special","general","possible",
            "important","interesting","available","ready","able","late","early","went","gone","going","coming",
            "asking","telling","looking","waiting","trying","using","making","getting","giving","taking","talking",
            "thinking","writing","reading","sending","testing","checking","fixing","building","changing","adding",
            "updating","running","playing","sleeping","eating","paying","opening","closing","moving","ended","started",
            "finished","finish","begin","began","continue","break","broke","broken","stuck","crash","crashed","down",
            "up","live","dead","exist","exists","happen","happened","happens","seem","seems","looks","sounds",
            "means","matter","matters","count","counts","stay","stayed","leave","left","arrive","return","visit",
            "join","share","shared","follow","followed","support","allow","agree","accept","decide","decided","choose",
            "chose","pick","prefer","expect","remember","forget","forgot","notice","noticed","understand","understood",
            "explain","describe","suggest","recommend","confirm","confirmed","cancel","cancelled","approve","approved",
            "reject","rejected","ignore","ignored","mention","mentioned","include","included","contain","contains",
            "require","required","provide","provided","receive","received","offer","offered","deliver","delivered",
            "produce","reduce","increase","improve","compare","measure","calculate","estimate","guess","bet","hope"
        };

        public static readonly HashSet<string> Hebrew = new HashSet<string>(StringComparer.Ordinal)
        {
            "שלום","תודה","בבקשה","סליחה","בוקר","ערב","לילה","טוב","טובה","יום","שבוע","חודש","שנה","היום","מחר","אתמול",
            "עכשיו","אחר","אחרי","לפני","מתי","איפה","למה","איך","כמה","אולי","בסדר","נכון","בטח","ברור","אני","אתה",
            "הוא","היא","אנחנו","אתם","שלי","שלך","שלו","שלה","שלנו","שלכם","שלהם","היה","היתה","הייתי","יהיה","תהיה",
            "צריך","צריכה","צריכים","רוצה","רוצים","יכול","יכולה","אפשר","חייב","חייבת",
            "בדיקה","בדיקות","לבדוק","בדקתי","בודק","בודקת","תיקון","לתקן","תיקנתי","בעיה","בעיות","תקלה","תקלות","שגיאה",
            "שגיאות","פתרון","עובד","עובדת","עבודה","לעבוד","קוד","גרסה","שרת","מערכת","לקוח","לקוחות","משתמש","משתמשים",
            "הזמנה","הזמנות","טיסה","טיסות","מלון","חבילה","חבילות","מחיר","מחירים","תשלום","כסף","חשבון","דוח","מסמך",
            "קובץ","קבצים","תמונה","הודעה","הודעות","מייל","פגישה","ישיבה","שיחה","טלפון","זמן","שעה","דקה","דקות","רגע",
            "מהר","לאט","קצת","הרבה","מאוד","ממש","תמיד","לפעמים","פעם","שוב","כולם","משהו","מישהו","דבר","דברים",
            "מקום","בית","משרד","חדר","דרך","עיר","ארץ","ישראל","עולם","אנשים","איש","אישה","ילד","ילדה","ילדים","חבר",
            "חברה","משפחה","אבא","אמא","מנהל","מנהלת","צוות","פרויקט","משימה","משימות","רשימה","טבלה","נתונים","מידע",
            "פרטים","שינוי","שינויים","עדכון","עדכונים","לעדכן","עדכנתי","לשלוח","שלחתי","שולח","לקבל","קיבלתי","מקבל",
            "לראות","ראיתי","רואה","לדעת","יודע","יודעת","לחשוב","חושב","חושבת","להגיד","אמר","אמרה","אומר","לעשות",
            "עשיתי","עושה","ללכת","הולך","לבוא","באה","לנסות","ניסיתי","מנסה","להתחיל","התחלתי","לסיים","סיימתי","סיום",
            "התחלה","סוף","אמצע","ראשון","ראשונה","שני","שנייה","אחרון","אחרונה","חדש","חדשה","ישן","גדול","גדולה","קטן",
            "קטנה","חשוב","חשובה","מוכן","מוכנה","מצוין","יופי","סבבה","מעולה","נהדר","בהצלחה","רבה","להתראות","שבת",
            "חג","שמח","מזל","שלומך","קורה","נראה","בסוף","בגלל","בשביל","כדי","אבל","אולם","כאשר","כשה","עם","בלי",
            "בין","על","תחת","ליד","מול","דרך","אצל","מאיפה","לאן","מדוע","כמובן","בדיוק","באמת","כנראה","כמעט","בערך"
        };
    }
}
