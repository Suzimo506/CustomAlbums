using System;
using System.Collections.Generic;
using Il2CppAssets.Scripts.Database;

namespace CustomAlbums.Utilities
{
    internal static class I18n
    {
        private const string FallbackLanguage = "en";
        private static string _currentLanguage = FallbackLanguage;

        private static readonly Dictionary<int, string> GameLanguageMap = new()
        {
            { 0, "zh-CN" },
            { 1, "en" },
            { 2, "zh-CN" },
            { 3, "zh-TW" },
            { 4, "ja" },
            { 5, "ko" }
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
        {
            ["custom_albums.title"] = Register("Custom Albums", "自定义", "自定義", "カスタムアルバム", "커스텀앨범"),
            ["library.entry_button"] = Register("Import Charts", "导入自制谱", "匯入自製譜", "カスタム譜面", "커스텀 보면"),
            ["library.close"] = Register("Close", "关闭", "關閉", "閉じる", "닫기"),
            ["library.count"] = Register("Candidates {0} / Imported {1}", "候选 {0} / 已导入 {1}", "候選 {0} / 已匯入 {1}", "候補 {0} / 導入済み {1}", "후보 {0} / 가져옴 {1}"),
            ["library.count_skipped"] = Register("Candidates {0} / Imported {1} / Skipped {2}", "候选 {0} / 已导入 {1} / 跳过 {2}", "候選 {0} / 已匯入 {1} / 跳過 {2}", "候補 {0} / 導入済み {1} / スキップ {2}", "후보 {0} / 가져옴 {1} / 건너뜀 {2}"),
            ["library.import_category"] = Register("Import Category", "导入分类", "匯入分類", "分類を導入", "분류 가져오기"),
            ["library.remove_category"] = Register("Remove Category", "移除分类", "移除分類", "分類を削除", "분류 제거"),
            ["library.importing"] = Register("Importing", "导入中", "匯入中", "導入中", "가져오는 중"),
            ["library.removing"] = Register("Removing", "移除中", "移除中", "削除中", "제거하는 중"),
            ["library.category_no_imported"] = Register("No imported charts in this category", "当前分类没有已导入谱面", "目前分類沒有已匯入譜面", "この分類に導入済み譜面はありません", "이 분류에 가져온 보면이 없습니다"),
            ["library.category_all_imported"] = Register("This category is fully imported", "当前分类已全部导入", "目前分類已全部匯入", "この分類はすべて導入済みです", "이 분류는 모두 가져왔습니다"),
            ["library.progress_import"] = Register("Importing in batches", "正在分批导入", "正在分批匯入", "分割導入中", "나누어 가져오는 중"),
            ["library.progress_remove"] = Register("Removing in batches", "正在分批移除", "正在分批移除", "分割削除中", "나누어 제거하는 중"),
            ["library.completed_import"] = Register("Imported {0} charts", "已导入 {0} 张谱面", "已匯入 {0} 張譜面", "{0}個の譜面を導入しました", "{0}개 보면을 가져왔습니다"),
            ["library.completed_remove"] = Register("Removed {0} charts", "已移除 {0} 张谱面", "已移除 {0} 張譜面", "{0}個の譜面を削除しました", "{0}개 보면을 제거했습니다"),
            ["library.search_placeholder"] = Register("Search title, artist, tags...", "搜索谱名、作者、标签...", "搜尋譜名、作者、標籤...", "曲名、作者、タグを検索...", "제목, 작가, 태그 검색..."),
            ["library.filter"] = Register("Filter", "筛选", "篩選", "絞り込み", "필터"),
            ["library.min"] = Register("Min", "最低", "最低", "最低", "최소"),
            ["library.max"] = Register("Max", "最高", "最高", "最高", "최대"),
            ["library.no_cover"] = Register("NO COVER", "无封面", "無封面", "NO COVER", "커버 없음"),
            ["library.preview_title"] = Register("Select a chart", "选择一张谱面", "選擇一張譜面", "譜面を選択", "보면 선택"),
            ["library.preview_demo_hint"] = Register("Demo plays after selection", "demo 会在选中后播放", "demo 會在選中後播放", "選択後にデモを再生します", "선택 후 데모가 재생됩니다"),
            ["library.loading"] = Register("Loading chart library...", "正在加载谱面库...", "正在載入譜面庫...", "譜面ライブラリ読み込み中...", "보면 라이브러리 로딩 중..."),
            ["library.load_failed"] = Register("Failed to load chart library. Check the MelonLoader log.", "谱面库加载失败，请查看 MelonLoader 日志", "譜面庫載入失敗，請查看 MelonLoader 日誌", "譜面ライブラリの読み込みに失敗しました。MelonLoaderログを確認してください", "보면 라이브러리 로딩 실패. MelonLoader 로그를 확인하세요"),
            ["library.empty"] = Register("No charts found", "没有找到谱面", "沒有找到譜面", "譜面が見つかりません", "보면을 찾을 수 없습니다"),
            ["library.import"] = Register("Import", "导入", "匯入", "導入", "가져오기"),
            ["library.remove"] = Register("Remove", "移除", "移除", "削除", "제거"),
            ["library.not_selected"] = Register("No chart selected", "未选择谱面", "未選擇譜面", "譜面未選択", "선택한 보면 없음"),
            ["library.select_from_list"] = Register("Select a chart from the list on the right", "从右侧列表选择一张谱面", "從右側列表選擇一張譜面", "右側のリストから譜面を選択してください", "오른쪽 목록에서 보면을 선택하세요"),
            ["library.label_difficulty"] = Register("Difficulty", "难度", "難度", "難易度", "난이도"),
            ["library.label_charter"] = Register("Charter", "谱师", "譜師", "譜面制作者", "채보 제작자"),
            ["library.label_category"] = Register("Category", "分类", "分類", "分類", "분류"),
            ["library.status_imported"] = Register("Imported", "已导入", "已匯入", "導入済み", "가져옴"),
            ["library.status_not_imported"] = Register("Not Imported", "未导入", "未匯入", "未導入", "가져오지 않음"),
            ["library.category_all"] = Register("All", "全部", "全部", "すべて", "전체"),
            ["library.category_active"] = Register("Imported", "已导入", "已匯入", "導入済み", "가져옴"),
            ["library.category_unsorted"] = Register("Unsorted", "未分类", "未分類", "未分類", "미분류"),
            ["hotreload.added"] = Register("Added {0} charts", "添加了 {0} 张谱面", "添加了 {0} 張譜面", "{0}個の譜面を追加しました", "{0}개 보면을 추가했습니다"),
            ["hotreload.deleted"] = Register("Deleted {0} charts", "删除了 {0} 张谱面", "刪除了 {0} 張譜面", "{0}個の譜面を削除しました", "{0}개 보면을 삭제했습니다"),
            ["rank.hq_missing"] = Register("Headquarters mod is not loaded! ~(*´Д｀)", "未加载 Headquarters 模组！~(*´Д｀)", "未載入 Headquarters 模組！~(*´Д｀)", "Headquarters MODが読み込まれていません！~(*´Д｀)", "Headquarters 모드가 로드되지 않았습니다! ~(*´Д｀)"),
            ["rank.crash"] = Register("CRASH: {0}\nCheck MelonLoader console for details.", "崩溃：{0}\n请查看 MelonLoader 控制台获取详情。", "崩潰：{0}\n請查看 MelonLoader 主控台取得詳情。", "クラッシュ：{0}\n詳細はMelonLoaderコンソールを確認してください。", "충돌: {0}\n자세한 내용은 MelonLoader 콘솔을 확인하세요.")
        };

        public static string Get(string key)
        {
            DetectLanguage();
            if (!Strings.TryGetValue(key, out var values)) return key;
            if (values.TryGetValue(_currentLanguage, out var localized)) return localized;
            return values.TryGetValue(FallbackLanguage, out var fallback) ? fallback : key;
        }

        public static string Format(string key, params object[] args)
        {
            var template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private static void DetectLanguage()
        {
            try
            {
                var gameLanguage = (int)GlobalDataBase.s_DbUi.curLanguageIndex;
                _currentLanguage = GameLanguageMap.TryGetValue(gameLanguage, out var language)
                    ? language
                    : FallbackLanguage;
            }
            catch
            {
                _currentLanguage = FallbackLanguage;
            }
        }

        private static Dictionary<string, string> Register(string en, string zhCn, string zhTw, string ja, string ko)
        {
            return new Dictionary<string, string>
            {
                { "en", en },
                { "zh-CN", zhCn },
                { "zh-TW", zhTw },
                { "ja", ja },
                { "ko", ko }
            };
        }
    }
}
