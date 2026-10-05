using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — <see cref="RunSaveData"/>를 파일로 읽고 쓰고 지운다. 파일을 만지는 곳은 여기 한 곳뿐이다.
///
/// 위치: <c>Application.persistentDataPath/run_save.json</c>
/// (Windows 빌드: <c>%USERPROFILE%\AppData\LocalLow\Gaksultang\재의 길\</c>). 회사 이름을 배포 전에 정한 이유가 여기서도 나온다 —
/// 회사 이름이 바뀌면 이 경로가 바뀌어 저장한 판이 사라진 것처럼 보인다. <b>에디터도 같은 경로를 쓴다</b>(빌드와 저장 파일을 나눠 쓴다).
///
/// <b>실패해도 게임을 멈추지 않는다.</b> 디스크가 꽉 찼거나 파일이 깨졌다고 예외가 터지면 플레이 중인 판이 멈춘다.
/// 저장은 있으면 좋은 기능이지 판을 걸 일이 아니므로, 실패는 경고만 남기고 false를 돌려준다.
///
/// 경로를 받는 함수를 따로 둔 이유: 테스트가 실제 저장 파일을 건드리지 않고 임시 폴더에서 시험하게 하려는 것이다.
/// </summary>
public static class RunSaveStore
{
    private const string FileName = "run_save.json";

    /// <summary>실제 저장 파일 경로.</summary>
    public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>저장한 판이 있는가(형식 검사 없이 파일만 본다).</summary>
    public static bool Exists() => File.Exists(DefaultPath);

    public static bool Save(RunSaveData data) => Save(data, DefaultPath);
    public static bool TryLoad(out RunSaveData data) => TryLoad(DefaultPath, out data);
    public static void Delete() => Delete(DefaultPath);

    /// <summary>
    /// 저장한다. <b>임시 파일에 다 쓴 뒤 바꿔치기</b>한다.
    /// 원본에 바로 쓰다가 그 순간 게임이 꺼지면(정전·강제 종료) 반쯤 쓰인 파일만 남아 이전 저장까지 잃는다.
    /// 다 쓴 임시 파일을 한 번에 바꾸면, 꺼지는 순간이 언제든 "이전 저장"이나 "새 저장" 둘 중 하나는 온전하다.
    /// </summary>
    public static bool Save(RunSaveData data, string path)
    {
        if (data == null) return false;

        try
        {
            data.version = RunSaveData.CurrentVersion;
            string json = JsonUtility.ToJson(data, prettyPrint: true);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);

            // File.Replace는 대상이 있어야 한다. 처음 저장할 때는 옮기기만 하면 된다.
            if (File.Exists(path)) File.Replace(tempPath, path, null);
            else File.Move(tempPath, path);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[저장] 판 저장 실패 — 이번 저장은 건너뛴다: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 불러온다. 파일이 없거나, 깨졌거나, 형식 번호가 다르면 false.
    /// 깨진 파일은 다음 저장 때 덮어써지므로 여기서 지우지 않는다(원인을 볼 수 있게 남겨 둔다).
    /// </summary>
    public static bool TryLoad(string path, out RunSaveData data)
    {
        data = null;
        if (!File.Exists(path)) return false;

        try
        {
            data = JsonUtility.FromJson<RunSaveData>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[저장] 저장 파일을 읽지 못했다(깨진 파일): " + e.Message);
            data = null;
            return false;
        }

        if (data == null) return false;

        if (data.version != RunSaveData.CurrentVersion)
        {
            Debug.LogWarning($"[저장] 저장 형식이 다르다(파일 {data.version}, 게임 {RunSaveData.CurrentVersion}) — 이어하지 않는다.");
            data = null;
            return false;
        }

        return true;
    }

    /// <summary>지운다. 판이 끝났거나(사망·클리어) 새 판을 시작할 때. 파일이 없어도 괜찮다.</summary>
    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[저장] 저장 파일 삭제 실패: " + e.Message);
        }
    }
}
