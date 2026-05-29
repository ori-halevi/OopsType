# אפיון: אופטימיזציה של תווית העכבר (MouseLabel Overlay)

> מסמך זה מיועד לסוכן ה‑AI שבונה את התוכנה. הוא עצמאי ומכיל גם את **ההחלטה** (מה לבנות) וגם את **הנימוקים** (למה דווקא כך), כדי שלא "תייעל" החוצה בטעות חלקים קריטיים. קרא את כולו לפני שאתה נוגע בקוד.

---

## 1. הקשר ומטרה

התוכנה היא overlay (חלון שקוף שמרחף מעל שאר התוכן) שמציג תווית הצמודה לסמן העכבר ועוקבת אחריו. **התוכנה דלוקה 24/7**, ולכן צריכת המשאבים בזמן שהמשתמש לא זז היא שיקול ראשון במעלה, לצד חלקות מושלמת בזמן תנועה.

**המטרה המנחה:** אפס עבודה מוחלט כשהעכבר נייח, וחלקות מסונכרנת‑מסך כשהעכבר זז.

הקבצים הרלוונטיים בקוד הקיים (נקודות העבודה):
- `Services/Overlays/MouseOverlayPresenter.cs` — לוגיקת המעקב (כאן הטיימר הנוכחי שמוחלף).
- `Views/OverlayWindowBase.cs` — חלון ה‑overlay (`AllowsTransparency`, `WS_EX_LAYERED`, ההזזה, `GetDpi`, `EnsureTopmost`).
- `Native/LowLevelKeyboardHook.cs` — תשתית hook קיימת לרמה‑נמוכה; יש להשתמש בה כתבנית ל‑mouse hook.

---

## 2. מה הבעיה במצב הקיים (רקע, לא לשחזר)

המימוש הנוכחי משתמש ב‑`DispatcherTimer` שרץ ב‑40Hz בעדיפות `DispatcherPriority.Background`, ובכל טיק קורא `GetCursorPos` ומזיז את החלון דרך `Left`/`Top` של WPF. שלושה גורמים מצטברים יוצרים רעד (jitter):

1. **קצב נמוך ולא מסונכרן למסך.** 40Hz נמוך בהרבה מקצב הדגימה של עכבר (125–1000Hz) ומקצב הרענון של מסך (60/120/144Hz). בתנועה מהירה הסמן קופץ 10–25px בין טיקים והתווית "מדביקה" בקפיצות.
2. **עדיפות `Background`.** הטיקים נדחים אחרי עבודת UI אחרת, כך שהמרווח בין הזזות אינו אחיד — וזה נתפס בעין כרעד, גרוע יותר מעיכוב קבוע.
3. **חלון Layered + `AllowsTransparency`.** משמעותי בעיקר כשמצוירים מחדש תכנים; הזזת מיקום עצמה מטופלת בזול ע"י DWM (ראה §6).

> **חשוב:** אל תפתור את זה רק ע"י העלאת קצב הטיימר ושינוי priority. זה "פלסטר" — עדיין דגימה לא מסונכרנת, ובמצב 24/7 גם בזבוז מתמשך. הפתרון הנכון בהמשך.

---

## 3. ההחלטה — הארכיטקטורה

שילוב של שלושה רכיבים, כל אחד עושה רק את מה שהוא טוב בו:

1. **Low‑level mouse hook (`WH_MOUSE_LL`)** — מזהה *מתי* העכבר זז. event‑driven, עלות אפס כשהעכבר נייח. זה כל תפקידו.
2. **`CompositionTarget.Rendering`** — קובע *מתי מזיזים*: פעם אחת בכל frame של WPF, מסונכרן למחזור הקומפוזיציה של המסך.
3. **ביטול רישום בסרק (idle unsubscribe)** — אחרי שהעכבר עומד, מתנתקים מ‑`Rendering` וחוזרים לסרק מוחלט. אירוע ה‑hook הבא ירשום מחדש.

### זרימת הנתונים

```
[תנועת עכבר במערכת]
        │
        ▼
WH_MOUSE_LL callback  ───►  callback טריוויאלי בלבד:
(thread של ה-hook)          • שמור timestamp אחרון של תנועה
                            • אם לא רשומים ל-Rendering → בקש רישום (על ה-UI thread)
                            • קרא CallNextHookEx, חזור מיד
        │
        ▼
CompositionTarget.Rendering  ───►  פעם בכל frame, על ה-UI thread:
(כל ~16.67ms ב-60Hz)               • GetCursorPos → מיקום עדכני
                                   • חשב יעד (offset + DPI scale מהמטמון)
                                   • הזז את החלון
                                   • אם עברו > IdleTimeout בלי תנועה → בטל רישום, חזור לסרק
        │
        ▼
[סרק מוחלט עד התנועה הבאה]
```

**עיקרון מנחה לזכור:** ה‑hook **אף פעם לא מזיז** את החלון בעצמו. הזזה מתוך ה‑callback תקרה בתזמון של thread ה‑hook, לא מסונכרנת ל‑frame — וזה מחזיר את הרעד. ה‑hook רק *מעיר*; ההזזה תמיד קורית בתוך `Rendering`.

---

## 4. רכיבים והנחיות מימוש

### 4.1 ה‑Mouse Hook

- השתמש בתבנית של `Native/LowLevelKeyboardHook.cs` הקיימת; צור מקבילה ל‑`WH_MOUSE_LL` (`SetWindowsHookEx(WH_MOUSE_LL=14, proc, hMod, 0)`).
- ה‑callback מקבל בין היתר `WM_MOUSEMOVE`. **חובה שיהיה טריוויאלי:** רק לעדכן `lastMoveTimestamp`, לבקש רישום ל‑Rendering אם צריך, לקרוא `CallNextHookEx`, ולחזור מיד.
- **אזהרת `LowLevelHooksTimeout`:** זהו hook גלובלי — כל אירוע עכבר במערכת עובר דרכו. אם ה‑callback איטי, Windows יסיר אותו בשקט (timeout ברירת מחדל ~300ms, `HKCU\Control Panel\Desktop\LowLevelHooksTimeout`), וגם תפגע בתחושת העכבר של כל המערכת. שום עבודה כבדה ב‑callback.
- **Threading:** רישום ל‑`CompositionTarget.Rendering` חייב לקרות על ה‑UI thread (אירוע WPF סטטי הקשור ל‑Dispatcher). אם ה‑hook לא על ה‑UI thread, מרשלים את הרישום דרך `Dispatcher.BeginInvoke`. אם משתמשים בתבנית הקיימת שמתקינה מ‑UI thread — אין צורך במרשלינג, אך יש לוודא שה‑callback לא נחסם ע"י עומס UI; אם זה חשש, הרץ את ה‑hook על thread ייעודי עם message pump משלו.

### 4.2 לולאת ההזזה ב‑`CompositionTarget.Rendering`

- בכל קריאה: `GetCursorPos` → חשב מיקום יעד (offset קבוע של התווית מהסמן + הכפלה ב‑DPI scale מהמטמון) → הזז.
- **idle detection:** החזק `lastMoveTimestamp` (מתעדכן ב‑hook). בכל frame בדוק `now - lastMoveTimestamp`. אם עבר `IdleTimeout` (ברירת מחדל ~150ms) — בטל את הרישום ל‑`Rendering` וחזור לסרק. השתמש ב‑`Stopwatch`/timestamps ולא בספירת frames, כדי שיהיה בלתי‑תלוי בקצב הרענון.
- ודא idempotency: רישום כפול ל‑`Rendering` אסור; החזק דגל `isSubscribed` עם הגנה מפני מרוצי threads.

### 4.3 שיטת ההזזה

- **מימוש ראשוני: דרך `Left`/`Top` של WPF.** פשוט, ושומר על טיפול ה‑DPI הטבעי של WPF. עיקר הרווח של הארכיטקטורה הוא ה‑hook + סנכרון ה‑frame, לא המיקרו‑אופטימיזציה הזו.
- **אופטימיזציה אופציונלית מתועדת:** אם פרופיילינג מראה ש‑layout pass של WPF עולה זמן ניכר בכל הזזה, החלף ל‑`SetWindowPos` ישיר (כבר בשימוש ב‑`EnsureTopmost`) עם הדגלים `SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE`. זה חוסך את שלב ה‑layout. **למדוד לפני שמאמצים** — אל תקפוץ לזה ללא ראיה.

### 4.4 מטמון DPI

- חשב את ה‑DPI scale **פעם אחת** במקום בכל הזזה (היום `GetDpi()` רץ בכל מהלך עם `PresentationSource.FromVisual` + traversal — מיותר).
- **קריטי לריבוי מסכים:** מטמון חד‑פעמי נאיבי נשבר כשהחלון עובר למסך עם DPI שונה. **בטל ורענן את המטמון** באירועי שינוי DPI/מסך (`DpiChanged` של WPF, או `WM_DPICHANGED`). זו דרישת נכונות, לא רק אופטימיזציה.

### 4.5 שקיפות — לא נוגעים

- שמור על `AllowsTransparency=true` ועל הרקע `#CC222222` (74% אטום) והפינות המעוגלות. הזזת חלון שקוף מטופלת ע"י DWM על ה‑GPU וזולה; היקר הוא ציור‑מחדש של *תוכן*, שלא קורה כשרק זזים. ויתור על שקיפות הוא מוצא אחרון בלבד ואינו חלק מאפיון זה.

---

## 5. הגדרות שנחשפות למשתמש (חלון ההגדרות)

עיקרון: אופטימיזציה טובה ל‑24/7 = **דיפולטים מצוינים, לא ערימת כפתורים**. אל תחשוף פרטי מימוש (קצב, priority, Left/Top מול SetWindowPos, מטמון DPI) — הם רק יבלבלו ויצרו תקלות תמיכה.

**חשוף בדיוק בחירה אחת משמעותית — מצב מעקב:**

- **חסכוני (ברירת מחדל):** הארכיטקטורה של §3 — סרק מוחלט כשהעכבר נייח. הכי טוב ל‑24/7 וללפטופים.
- **חלקות מקסימלית:** רישום קבוע ל‑`CompositionTarget.Rendering` בלי ביטול. תגובה מהירה במעט יותר ב‑frame הראשון של תנועה, על חשבון צריכת מעבד/סוללה ברקע. מתאים רק לדסקטופ חזק.

טקסט הסבר מוצע ליד ההגדרה:

> **אופן מעקב הסמן**
> *חסכוני* — התווית פעילה רק כשהעכבר זז ואינה צורכת משאבים במנוחה (מומלץ, במיוחד בלפטופ).
> *חלקות מקסימלית* — תגובה מהירה במעט יותר על חשבון צריכת מעבד/סוללה גבוהה יותר ברקע.

`IdleTimeout` נשאר דיפולט פנימי (~150ms) ו**אינו** נחשף, אלא אם יתברר בהמשך שיש בו צורך אמיתי; אז כ‑knob "מתקדם" בלבד.

---

## 6. מה משתנה בקוד

- **להסיר:** את ה‑`DispatcherTimer` (40Hz, `Background`) ב‑`MouseOverlayPresenter.cs` ואת לולאת הטיק שלו.
- **להוסיף:** mouse hook (`WH_MOUSE_LL`) לפי תבנית `LowLevelKeyboardHook.cs`; מנוי/ביטול מנוי ל‑`CompositionTarget.Rendering`; לוגיקת idle.
- **לשנות ב‑`OverlayWindowBase.cs`:** הזזה (ראשונית דרך `Left`/`Top`; אופציונלית `SetWindowPos`), מטמון DPI עם ריענון באירועי DPI/מסך.
- **לא לשנות:** הגדרות השקיפות והמראה הוויזואלי.

---

## 7. אלטרנטיבות שנדחו (כדי שלא יחזרו)

- **רק להעלות את קצב הטיימר + priority `Render`:** "פלסטר". עדיין דגימה לא מסונכרנת, ובמצב 24/7 בזבוז מתמשך. נדחה.
- **רק `CompositionTarget.Rendering` בלי hook (פולינג בתוך ה‑frame, בלי ביטול רישום):** פתרון מצוין לחלקות, אבל מכריח רינדור רציף ~60fps כל עוד רשומים — גם כשהעכבר עומד. ל‑24/7 זה GPU/CPU שלא נחים אף פעם. נדחה כברירת מחדל; נשאר רק כאופציית "חלקות מקסימלית".
- **הזזה ישירות מתוך ה‑hook callback:** מחזיר רעד (תזמון לא מסונכרן ל‑frame) ומסכן את `LowLevelHooksTimeout`. נדחה.
- **ויתור על `AllowsTransparency`:** הגורם הכבד רק בציור תוכן, לא בהזזה; tradeoff ויזואלי כבד. מוצא אחרון, מחוץ לאפיון.

---

## 8. קריטריוני קבלה (Definition of Done)

1. **סרק:** כשהעכבר נייח אין עבודה לכל frame — אין טיקים של `CompositionTarget.Rendering`, צריכת CPU של התהליך ≈0, אין רינדור רציף.
2. **תנועה:** התווית עוקבת חלק, עדכונים מסונכרנים לקצב הרענון, ללא רעד נראה לעין גם בתנועות מהירות.
3. **חזרה לסרק:** תוך ~150ms מעצירת העכבר התהליך חוזר לסרק מוחלט (מבטל רישום).
4. **Hook:** ה‑callback חוזר הרבה מתחת ל‑`LowLevelHooksTimeout`; תחושת העכבר של כל המערכת אינה נפגעת.
5. **ריבוי מסכים / DPI מעורב:** מיקום נכון בכל מסך; מטמון ה‑DPI מתרענן במעבר מסך/שינוי DPI.
6. **מראה:** שקיפות, `#CC222222` והפינות המעוגלות נשמרים ללא שינוי.
7. **הגדרות:** "חסכוני" הוא ברירת המחדל; "חלקות מקסימלית" שומר רישום רציף ל‑`Rendering`. שאר המנגנון אינו נחשף.

---

## 9. שלד פסאודו‑קוד (להמחשה בלבד — התאם למבנה הקיים)

```csharp
// MouseOverlayPresenter (סכמטי)
private bool _isSubscribed;
private long _lastMoveTicks;              // Stopwatch.GetTimestamp()
private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(150);
private bool _maxSmoothness;              // מ-config; אם true: לא מבטלים רישום

// callback של ה-hook — חייב להיות טריוויאלי
private IntPtr OnMouseHook(int code, IntPtr wParam, IntPtr lParam)
{
    if (code >= 0 /* && wParam == WM_MOUSEMOVE */)
    {
        Interlocked.Exchange(ref _lastMoveTicks, Stopwatch.GetTimestamp());
        EnsureSubscribed();   // מרשל ל-UI thread אם צריך
    }
    return CallNextHookEx(_hookId, code, wParam, lParam);
}

private void EnsureSubscribed()
{
    if (_isSubscribed) return;
    // אם לא על UI thread: Dispatcher.BeginInvoke(EnsureSubscribed)
    _isSubscribed = true;
    CompositionTarget.Rendering += OnRendering;
}

private void OnRendering(object? s, EventArgs e)
{
    GetCursorPos(out var p);
    MoveOverlayTo(p);                      // Left/Top (או SetWindowPos אם נמדד צורך) + DPI מהמטמון

    if (_maxSmoothness) return;            // מצב "חלקות מקסימלית": לא מבטלים רישום

    var idle = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastMoveTicks));
    if (idle > IdleTimeout)
    {
        CompositionTarget.Rendering -= OnRendering;
        _isSubscribed = false;             // סרק מוחלט עד אירוע ה-hook הבא
    }
}
```

---

## 10. תקציר בשורה אחת

Hook לזיהוי תנועה → הזזה מסונכרנת ל‑frame דרך `CompositionTarget.Rendering` → ביטול רישום בסרק → מטמון DPI עם ריענון לפי מסך → שקיפות נשמרת → חושפים למשתמש בחירה אחת בלבד (חסכוני / חלקות מקסימלית), עם "חסכוני" כברירת מחדל.
