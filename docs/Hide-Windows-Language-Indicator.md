# הסתרת אינדיקטור השפה של Windows משורת המשימות

מדריך קצר להסתרת מחוון השפה/קלט המובנה של Windows (מתג ENG/HEB) משורת המשימות,
כדי להסתמך על המחוונים של OopsType ולפנות מקום.

> זוהי הגדרה של Windows, לא של OopsType — לכן עושים אותה פעם אחת ידנית.
> OopsType אינו יכול לבצע אותה אוטומטית באופן אמין (ראו "למה ידני" בהמשך).

## שלבים

1. פתח **Settings** (הגדרות Windows).
2. **Time & language** → **Typing**.
3. גלול למטה → **Advanced keyboard settings**.
4. סמן ✔ **"Use the desktop language bar when it's available"**.
5. לחץ על **"Language bar options"**.
6. בחלון שנפתח: טאב **"Language Bar"** → בחר **"Hidden"**.
7. לחץ **OK**.

המחוון אמור להיעלם מיד משורת המשימות.

## לשחזור (להחזיר את המחוון)

חזור לשלב 6 ובחר **"Floating On Desktop"** או **"Docked in the taskbar"**,
או הסר את הסימון בשלב 4.

## למה ידני ולא דרך התוכנה

ניסינו לאוטמט את זה, ונתקלנו במגבלת ארכיטקטורה של Windows:
- את מצב **"Hidden"** אפשר לכתוב לרגיסטרי (`HKCU\Software\Microsoft\CTF\LangBar\ShowStatus = 3`).
- אבל את **"Use the desktop language bar when it's available"** Windows **לא** שומר כערך רגיסטרי
  פשוט שאפשר לכתוב — בלעדיו, מצב "Hidden" לבדו לא מסתיר את המחוון המודרני.
- ההחלה "החיה" דרך ה-API היחיד שזמין (`ITfLangBarMgr::ShowFloating`) תחומה לחלון שבפוקוס
  וחוזרת ברגע שעוברים לאפליקציה אחרת.

לכן עדיף שלב ידני חד-פעמי על פני קוד שביר שעלול להישבר בעדכון Windows עתידי.

## אם המחוון חוזר

אם אחרי אתחול או עדכון Windows גדול המחוון חוזר — פשוט חזור על שלבים 4–7.
