using System.IO;

namespace RdpManager.Services;

/// <summary>
/// クラッシュ/電源断でファイルが途中状態にならないよう、temp に書いてから置換する。
/// 書き込み中の中断では既存ファイルは無傷のまま残る。
/// </summary>
public static class AtomicWrite
{
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
        {
            // バックアップを介して既存ファイルを原子的に差し替える
            File.Replace(tmp, path, path + ".prev", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}
