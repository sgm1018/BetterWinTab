namespace BetterWinTab.Services;

public static class RecycleBinMatcher
{
    private static readonly string[] Terms =
    [
        // English
        "recycle bin", "recycling bin", "trash", "trash can", "rubbish", "rubbish bin", "bin", "deleted", "deleted files",
        // Spanish
        "papelera", "papelera de reciclaje", "basura", "eliminados", "archivos eliminados",
        // French
        "corbeille", "poubelle", "fichiers supprimes",
        // German
        "papierkorb", "mülleimer", "gelöschte dateien",
        // Italian
        "cestino", "pattumiera", "file eliminati",
        // Portuguese
        "lixeira", "lixo", "arquivos excluidos",
        // Russian
        "корзина", "удаленные файлы",
        // Chinese
        "回收站", "垃圾箱", "已删除",
        // Japanese
        "ゴミ箱", "削除済み",
        // Korean
        "휴지통", "삭제된 파일",
        // Dutch
        "prullenbak", "verwijderde bestanden",
        // Polish
        "kosz", "usunięte pliki",
        // Turkish
        "geri dönüşüm kutusu", "çöp kutusu", "silinen dosyalar",
        // Arabic
        "سلة المحذوفات", "سلة المهملات",
        // Swedish
        "papperskorg", "borttagna filer",
        // Hindi
        "रिसाइकिल बिन", "हटाई गई फ़ाइलें",
    ];

    public static bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var q = query.Trim().ToLowerInvariant();
        return Terms.Any(t => t.Contains(q) || q.Contains(t));
    }
}
