package com.novabrowser.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.res.Configuration;

import org.json.JSONArray;
import org.json.JSONObject;

import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.List;

public final class NovaStore {
    private static final String PREFS = "nova_mobile_prefs";
    private static final String KEY_THEME = "theme_mode";
    private static final String KEY_HOME = "home_page";
    private static final String KEY_ENGINE = "search_engine";
    private static final String KEY_BOOKMARKS = "bookmarks";
    private static final String KEY_HISTORY = "history";

    public static final String[] ENGINES = {"Google", "Яндекс", "DuckDuckGo", "Bing"};

    public static final class Entry {
        public final String title;
        public final String url;
        public final long time;

        Entry(String title, String url, long time) {
            this.title = title == null ? "" : title;
            this.url = url == null ? "" : url;
            this.time = time;
        }
    }

    private final SharedPreferences prefs;
    private final Context context;

    public NovaStore(Context context) {
        this.context = context.getApplicationContext();
        this.prefs = this.context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    public String getThemeMode() {
        return prefs.getString(KEY_THEME, "system");
    }

    public void setThemeMode(String mode) {
        prefs.edit().putString(KEY_THEME, mode).apply();
    }

    public boolean isDark() {
        String mode = getThemeMode();
        if ("dark".equals(mode)) return true;
        if ("light".equals(mode)) return false;
        int uiMode = context.getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        return uiMode == Configuration.UI_MODE_NIGHT_YES;
    }

    public String getHomePage() {
        return prefs.getString(KEY_HOME, "");
    }

    public void setHomePage(String value) {
        prefs.edit().putString(KEY_HOME, value == null ? "" : value.trim()).apply();
    }

    public String getSearchEngine() {
        String value = prefs.getString(KEY_ENGINE, "Google");
        for (String engine : ENGINES) {
            if (engine.equals(value)) return value;
        }
        return "Google";
    }

    public void setSearchEngine(String value) {
        prefs.edit().putString(KEY_ENGINE, value).apply();
    }

    public String searchUrl(String query) {
        String q;
        try {
            q = URLEncoder.encode(query, "UTF-8");
        } catch (Exception e) {
            q = query;
        }
        String engine = getSearchEngine();
        if ("Яндекс".equals(engine)) return "https://yandex.ru/search/?text=" + q;
        if ("DuckDuckGo".equals(engine)) return "https://duckduckgo.com/?q=" + q;
        if ("Bing".equals(engine)) return "https://www.bing.com/search?q=" + q;
        return "https://www.google.com/search?q=" + q;
    }

    public synchronized List<Entry> getBookmarks() {
        return readEntries(KEY_BOOKMARKS);
    }

    public synchronized String getBookmarksJson() {
        return toJson(getBookmarks());
    }

    public synchronized boolean isBookmarked(String url) {
        if (url == null || url.isEmpty()) return false;
        for (Entry e : readEntries(KEY_BOOKMARKS)) {
            if (url.equals(e.url)) return true;
        }
        return false;
    }

    public synchronized void addBookmark(String title, String url) {
        if (url == null || url.isEmpty() || isNewTab(url)) return;
        List<Entry> items = readEntries(KEY_BOOKMARKS);
        items.removeIf(e -> url.equals(e.url));
        items.add(0, new Entry(title, url, System.currentTimeMillis()));
        while (items.size() > 200) items.remove(items.size() - 1);
        writeEntries(KEY_BOOKMARKS, items);
    }

    public synchronized void removeBookmark(String url) {
        List<Entry> items = readEntries(KEY_BOOKMARKS);
        items.removeIf(e -> url.equals(e.url));
        writeEntries(KEY_BOOKMARKS, items);
    }

    public synchronized void clearBookmarks() {
        prefs.edit().remove(KEY_BOOKMARKS).apply();
    }

    public synchronized List<Entry> getHistory() {
        return readEntries(KEY_HISTORY);
    }

    public synchronized String getHistoryJson() {
        return toJson(getHistory());
    }

    public synchronized void addHistory(String title, String url) {
        if (url == null || url.isEmpty() || isNewTab(url)) return;
        if (!url.startsWith("http://") && !url.startsWith("https://")) return;
        List<Entry> items = readEntries(KEY_HISTORY);
        items.removeIf(e -> url.equals(e.url));
        items.add(0, new Entry(title, url, System.currentTimeMillis()));
        while (items.size() > 300) items.remove(items.size() - 1);
        writeEntries(KEY_HISTORY, items);
    }

    public synchronized void clearHistory() {
        prefs.edit().remove(KEY_HISTORY).apply();
    }

    private boolean isNewTab(String url) {
        return url.startsWith("file:///android_asset/newtab.html");
    }

    private List<Entry> readEntries(String key) {
        List<Entry> result = new ArrayList<>();
        String raw = prefs.getString(key, "[]");
        try {
            JSONArray array = new JSONArray(raw);
            for (int i = 0; i < array.length(); i++) {
                JSONObject object = array.optJSONObject(i);
                if (object == null) continue;
                String url = object.optString("url", "");
                if (url.isEmpty()) continue;
                result.add(new Entry(object.optString("title", ""), url, object.optLong("time", 0)));
            }
        } catch (Exception ignored) {
        }
        return result;
    }

    private void writeEntries(String key, List<Entry> entries) {
        JSONArray array = new JSONArray();
        for (Entry entry : entries) {
            try {
                JSONObject object = new JSONObject();
                object.put("title", entry.title);
                object.put("url", entry.url);
                object.put("time", entry.time);
                array.put(object);
            } catch (Exception ignored) {
            }
        }
        prefs.edit().putString(key, array.toString()).apply();
    }

    private String toJson(List<Entry> entries) {
        JSONArray array = new JSONArray();
        for (Entry entry : entries) {
            try {
                JSONObject object = new JSONObject();
                object.put("title", entry.title);
                object.put("url", entry.url);
                object.put("time", entry.time);
                array.put(object);
            } catch (Exception ignored) {
            }
        }
        return array.toString();
    }
}
