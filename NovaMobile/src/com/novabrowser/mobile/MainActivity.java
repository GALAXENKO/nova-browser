package com.novabrowser.mobile;

import android.app.Activity;
import android.app.Dialog;
import android.app.DownloadManager;
import android.content.Context;
import android.content.Intent;
import android.content.res.ColorStateList;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.Environment;
import android.text.InputType;
import android.text.TextUtils;
import android.text.format.Formatter;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.JavascriptInterface;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

public class MainActivity extends Activity implements DownloadListener, WebView.FindListener {

    private static final String START_PAGE = "file:///android_asset/newtab.html";
    private static final int REQUEST_FILE_CHOOSER = 121;
    private static final int MAX_TABS = 24;

    private NovaStore store;
    private NovaPalette palette;

    private FrameLayout root;
    private LinearLayout topBar;
    private LinearLayout addressWrap;
    private LinearLayout bottomBar;
    private FrameLayout webContainer;
    private EditText addressBar;
    private ImageView securityIcon;
    private ImageView reloadButton;
    private ProgressBar progressBar;
    private TextView tabBadge;
    private FrameLayout findBar;
    private TextView findStatus;
    private EditText findInput;
    private FrameLayout fullscreenView;

    private final List<ImageButton> navIcons = new ArrayList<>();
    private final List<Tab> tabs = new ArrayList<>();
    private int selectedIndex = -1;

    private Dialog activeDialog;
    private ValueCallback<Uri[]> filePathCallback;

    private static final class Tab {
        final WebView view;
        String url;
        String title;
        boolean isStart;

        Tab(WebView view, String url) {
            this.view = view;
            this.url = url;
            this.title = "Nova Browser";
            this.isStart = url.startsWith(START_PAGE);
        }
    }

    private interface ValueAction {
        void run(String value);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        store = new NovaStore(this);
        palette = new NovaPalette(store.isDark());
        setContentView(R.layout.activity_main);
        root = findViewById(R.id.nova_root);
        buildChrome();
        applyTheme();

        String home = store.getHomePage().trim();
        createTab(home.isEmpty() ? startPageUrl() : normalizeUrl(home), true);

        Intent intent = getIntent();
        if (intent != null && Intent.ACTION_VIEW.equals(intent.getAction()) && intent.getDataString() != null) {
            openInNewTab(intent.getDataString());
        }
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        if (intent != null && Intent.ACTION_VIEW.equals(intent.getAction()) && intent.getDataString() != null) {
            openInNewTab(intent.getDataString());
        }
    }

    // ---------------------------------------------------------------- chrome

    private void buildChrome() {
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        root.addView(column, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        topBar = new LinearLayout(this);
        topBar.setOrientation(LinearLayout.VERTICAL);
        column.addView(topBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout topContent = new LinearLayout(this);
        topContent.setOrientation(LinearLayout.HORIZONTAL);
        topContent.setGravity(Gravity.CENTER_VERTICAL);
        topContent.setPadding(dp(8), dp(6), dp(8), dp(6));
        topBar.addView(topContent, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        ImageView brand = new ImageView(this);
        brand.setImageResource(R.mipmap.ic_launcher);
        brand.setScaleType(ImageView.ScaleType.CENTER_CROP);
        LinearLayout.LayoutParams brandParams = new LinearLayout.LayoutParams(dp(34), dp(34));
        brandParams.rightMargin = dp(8);
        brand.setLayoutParams(brandParams);
        brand.setContentDescription("Nova Browser");
        brand.setOnClickListener(v -> openInNewTab(startPageUrl()));
        topContent.addView(brand);

        addressWrap = new LinearLayout(this);
        addressWrap.setOrientation(LinearLayout.HORIZONTAL);
        addressWrap.setGravity(Gravity.CENTER_VERTICAL);
        addressWrap.setPadding(dp(10), 0, dp(4), 0);
        addressWrap.setLayoutParams(new LinearLayout.LayoutParams(0, dp(44), 1f));
        topContent.addView(addressWrap);

        securityIcon = new ImageView(this);
        LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(dp(18), dp(18));
        iconParams.rightMargin = dp(6);
        securityIcon.setLayoutParams(iconParams);
        securityIcon.setContentDescription("Сайт");
        addressWrap.addView(securityIcon);

        addressBar = new EditText(this);
        addressBar.setBackground(null);
        addressBar.setSingleLine(true);
        addressBar.setMaxLines(1);
        addressBar.setEllipsize(TextUtils.TruncateAt.END);
        addressBar.setTextSize(15);
        addressBar.setPadding(0, 0, 0, 0);
        addressBar.setGravity(Gravity.CENTER_VERTICAL);
        addressBar.setImeOptions(EditorInfo.IME_ACTION_GO);
        addressBar.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        addressBar.setHint("Поиск или адрес");
        addressBar.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f));
        addressBar.setOnEditorActionListener((v, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_GO || actionId == EditorInfo.IME_ACTION_DONE) {
                submitAddress();
                return true;
            }
            return false;
        });
        addressBar.setOnFocusChangeListener((v, hasFocus) -> {
            if (hasFocus) {
                addressBar.selectAll();
            } else {
                updateAddressBar();
            }
        });
        addressWrap.addView(addressBar);

        reloadButton = new ImageButton(this);
        reloadButton.setBackground(null);
        reloadButton.setImageResource(R.drawable.ic_refresh);
        reloadButton.setContentDescription("Обновить");
        LinearLayout.LayoutParams reloadParams = new LinearLayout.LayoutParams(dp(34), dp(34));
        reloadButton.setLayoutParams(reloadParams);
        reloadButton.setOnClickListener(v -> {
            Tab tab = currentTab();
            if (tab != null) {
                tab.view.reload();
            }
        });
        addressWrap.addView(reloadButton);

        progressBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progressBar.setMax(100);
        progressBar.setProgress(0);
        topBar.addView(progressBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(3)));

        webContainer = new FrameLayout(this);
        column.addView(webContainer, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        bottomBar = new LinearLayout(this);
        bottomBar.setOrientation(LinearLayout.HORIZONTAL);
        bottomBar.setGravity(Gravity.CENTER);
        bottomBar.setPadding(dp(6), dp(4), dp(6), dp(6));
        column.addView(bottomBar, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(56)));

        bottomBar.addView(navItem(R.drawable.ic_back, "Назад", this::goBack));
        bottomBar.addView(navItem(R.drawable.ic_forward, "Вперёд", this::goForward));
        bottomBar.addView(navItem(R.drawable.ic_home, "Домой", this::goHome));
        bottomBar.addView(tabsItem());
        bottomBar.addView(navItem(R.drawable.ic_more, "Меню", this::showMenu));

        findBar = buildFindBar();
        FrameLayout.LayoutParams findParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM);
        findParams.bottomMargin = dp(56);
        root.addView(findBar, findParams);

        fullscreenView = new FrameLayout(this);
        fullscreenView.setBackgroundColor(Color.BLACK);
        fullscreenView.setVisibility(View.GONE);
        root.addView(fullscreenView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private View navItem(int iconRes, String description, Runnable action) {
        FrameLayout holder = new FrameLayout(this);
        holder.setLayoutParams(new LinearLayout.LayoutParams(0, dp(46), 1f));
        ImageButton button = new ImageButton(this);
        button.setBackground(null);
        button.setImageResource(iconRes);
        button.setContentDescription(description);
        button.setLayoutParams(new FrameLayout.LayoutParams(dp(24), dp(24), Gravity.CENTER));
        holder.addView(button);
        holder.setOnClickListener(v -> action.run());
        navIcons.add(button);
        return holder;
    }

    private View tabsItem() {
        FrameLayout holder = new FrameLayout(this);
        holder.setLayoutParams(new LinearLayout.LayoutParams(0, dp(46), 1f));
        ImageButton button = new ImageButton(this);
        button.setBackground(null);
        button.setImageResource(R.drawable.ic_tabs);
        button.setContentDescription("Вкладки");
        button.setLayoutParams(new FrameLayout.LayoutParams(dp(24), dp(24), Gravity.CENTER));
        holder.addView(button);
        holder.setOnClickListener(v -> showTabs());
        navIcons.add(button);

        tabBadge = new TextView(this);
        tabBadge.setTextSize(9);
        tabBadge.setTextColor(Color.WHITE);
        tabBadge.setGravity(Gravity.CENTER);
        FrameLayout.LayoutParams badgeParams = new FrameLayout.LayoutParams(dp(17), dp(17), Gravity.CENTER);
        badgeParams.leftMargin = dp(14);
        badgeParams.topMargin = dp(0);
        tabBadge.setLayoutParams(badgeParams);
        holder.addView(tabBadge);
        return holder;
    }

    private FrameLayout buildFindBar() {
        FrameLayout bar = new FrameLayout(this);
        bar.setVisibility(View.GONE);
        bar.setPadding(dp(10), dp(8), dp(10), dp(8));

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(12), dp(6), dp(6), dp(6));
        bar.addView(row, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        findInput = new EditText(this);
        findInput.setSingleLine(true);
        findInput.setHint("Найти на странице");
        findInput.setTextSize(15);
        findInput.setBackground(null);
        findInput.setPadding(0, 0, 0, 0);
        findInput.setImeOptions(EditorInfo.IME_ACTION_SEARCH);
        findInput.setInputType(InputType.TYPE_CLASS_TEXT);
        findInput.setLayoutParams(new LinearLayout.LayoutParams(0, dp(36), 1f));
        findInput.setOnEditorActionListener((v, actionId, event) -> {
            if (actionId == EditorInfo.IME_ACTION_SEARCH) {
                runFind(true);
                return true;
            }
            return false;
        });
        row.addView(findInput);

        findStatus = new TextView(this);
        findStatus.setTextSize(11);
        findStatus.setPadding(dp(6), 0, dp(6), 0);
        row.addView(findStatus);

        ImageButton prev = smallButton(R.drawable.ic_back, "Предыдущее совпадение", v -> runFind(false));
        ImageButton next = smallButton(R.drawable.ic_forward, "Следующее совпадение", v -> runFind(true));
        ImageButton close = smallButton(R.drawable.ic_close, "Закрыть поиск", v -> hideFindBar());
        row.addView(prev);
        row.addView(next);
        row.addView(close);

        bar.setTag(row);
        return bar;
    }

    private ImageButton smallButton(int iconRes, String description, View.OnClickListener listener) {
        ImageButton button = new ImageButton(this);
        button.setBackground(null);
        button.setImageResource(iconRes);
        button.setContentDescription(description);
        button.setLayoutParams(new LinearLayout.LayoutParams(dp(34), dp(34)));
        button.setOnClickListener(listener);
        return button;
    }

    // ---------------------------------------------------------------- theme

    private void applyTheme() {
        palette = new NovaPalette(store.isDark());

        root.setBackgroundColor(palette.bg);
        topBar.setBackgroundColor(palette.bg);
        bottomBar.setBackgroundColor(palette.nav);
        webContainer.setBackgroundColor(palette.webBg);
        addressWrap.setBackground(rounded(palette.address, dp(22)));

        addressBar.setTextColor(palette.text);
        addressBar.setHintTextColor(palette.muted);
        securityIcon.setColorFilter(palette.accent);
        reloadButton.setColorFilter(palette.accent);
        progressBar.setProgressTintList(ColorStateList.valueOf(palette.accent));

        for (ImageButton icon : navIcons) {
            icon.setColorFilter(palette.text);
        }

        if (tabBadge != null) {
            tabBadge.setBackground(rounded(palette.accent, dp(9)));
            tabBadge.setTextColor(palette.dark ? Color.parseColor("#0B1122") : Color.WHITE);
        }

        if (findBar != null) {
            findBar.setBackground(rounded(palette.surface, dp(18)));
            findInput.setTextColor(palette.text);
            findInput.setHintTextColor(palette.muted);
            findStatus.setTextColor(palette.muted);
            Object tag = findBar.getTag();
            if (tag instanceof ViewGroup) {
                ViewGroup row = (ViewGroup) tag;
                for (int i = 0; i < row.getChildCount(); i++) {
                    View child = row.getChildAt(i);
                    if (child instanceof ImageButton) {
                        ((ImageButton) child).setColorFilter(palette.text);
                    }
                }
            }
        }

        for (Tab tab : tabs) {
            tab.view.setBackgroundColor(palette.webBg);
        }

        Window window = getWindow();
        if (window != null) {
            window.setStatusBarColor(palette.bg);
            window.setNavigationBarColor(palette.nav);
            int flags = View.SYSTEM_UI_FLAG_VISIBLE;
            if (!palette.dark) {
                flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
                flags |= View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
            }
            window.getDecorView().setSystemUiVisibility(flags);
        }
    }

    private void reloadTheme() {
        applyTheme();
        String url = startPageUrl();
        for (Tab tab : tabs) {
            if (tab.isStart) {
                tab.url = url;
                tab.view.loadUrl(url);
            }
        }
    }

    // ---------------------------------------------------------------- tabs

    private WebView buildWebView() {
        WebView view = new WebView(this);
        WebSettings settings = view.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setSupportMultipleWindows(false);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setAllowContentAccess(true);
        settings.setLoadsImagesAutomatically(true);
        settings.setUseWideViewPort(true);
        settings.setLoadWithOverviewMode(true);
        settings.setBuiltInZoomControls(false);
        settings.setDisplayZoomControls(false);
        settings.setMediaPlaybackRequiresUserGesture(true);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);
        view.setBackgroundColor(palette.webBg);
        view.setWebViewClient(new NovaWebViewClient());
        view.setWebChromeClient(new NovaChromeClient());
        view.setFindListener(this);
        view.setDownloadListener(this);
        view.addJavascriptInterface(new NovaBridge(), "Nova");
        CookieManager.getInstance().setAcceptCookie(true);
        return view;
    }

    private Tab currentTab() {
        if (selectedIndex < 0 || selectedIndex >= tabs.size()) return null;
        return tabs.get(selectedIndex);
    }

    private Tab tabFor(WebView view) {
        for (Tab tab : tabs) {
            if (tab.view == view) return tab;
        }
        return null;
    }

    private Tab createTab(String url, boolean select) {
        if (tabs.size() >= MAX_TABS) {
            toast("Достигнут лимит вкладок");
            return null;
        }
        WebView view = buildWebView();
        Tab tab = new Tab(view, url);
        tabs.add(tab);
        if (select || selectedIndex < 0) {
            selectedIndex = tabs.size() - 1;
            showSelectedTab();
        }
        view.loadUrl(url);
        updateTabBadge();
        return tab;
    }

    private void showSelectedTab() {
        Tab tab = currentTab();
        if (tab == null) return;
        ViewGroup parent = (ViewGroup) tab.view.getParent();
        if (parent != null) parent.removeView(tab.view);
        webContainer.removeAllViews();
        webContainer.addView(tab.view, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        hideFindBar();
        updateAddressBar();
        updateNavButtons();
    }

    private void selectTab(int index) {
        if (index < 0 || index >= tabs.size()) return;
        selectedIndex = index;
        showSelectedTab();
        updateTabBadge();
    }

    private void closeTab(int index) {
        if (index < 0 || index >= tabs.size()) return;
        Tab removed = tabs.remove(index);
        try {
            removed.view.stopLoading();
            removed.view.destroy();
        } catch (Exception ignored) {
        }
        if (tabs.isEmpty()) {
            selectedIndex = -1;
            createTab(startPageUrl(), true);
            return;
        }
        if (selectedIndex > index) selectedIndex--;
        if (selectedIndex >= tabs.size()) selectedIndex = tabs.size() - 1;
        showSelectedTab();
        updateTabBadge();
    }

    private void updateTabBadge() {
        if (tabBadge == null) return;
        tabBadge.setText(String.valueOf(tabs.size()));
        tabBadge.setVisibility(tabs.size() > 1 ? View.VISIBLE : View.GONE);
    }

    private void updateNavButtons() {
        Tab tab = currentTab();
        boolean canBack = tab != null && tab.view.canGoBack();
        boolean canForward = tab != null && tab.view.canGoForward();
        if (navIcons.size() >= 2) {
            navIcons.get(0).setEnabled(canBack);
            navIcons.get(0).setAlpha(canBack ? 1f : 0.35f);
            navIcons.get(1).setEnabled(canForward);
            navIcons.get(1).setAlpha(canForward ? 1f : 0.35f);
        }
    }

    // ---------------------------------------------------------------- navigation

    private String startPageUrl() {
        return START_PAGE + "?theme=" + (palette.dark ? "dark" : "light") + "&nova=1";
    }

    private boolean isStartPage(String url) {
        return url != null && url.startsWith(START_PAGE);
    }

    private String normalizeUrl(String input) {
        String value = input == null ? "" : input.trim();
        if (value.isEmpty()) return startPageUrl();
        if (isStartPage(value)) return value;
        String lower = value.toLowerCase();
        if (lower.startsWith("http://") || lower.startsWith("https://") || lower.startsWith("file:")
                || lower.startsWith("content:") || lower.startsWith("about:") || lower.startsWith("data:")) {
            return value;
        }
        if (value.contains("://")) return value;
        if (!value.contains(" ") && (value.startsWith("www.") || value.matches("^[^\\s]+\\.[a-zA-Z]{2,}(/.*)?$"))) {
            return "https://" + value;
        }
        return store.searchUrl(value);
    }

    private void submitAddress() {
        String input = addressBar.getText().toString().trim();
        if (input.isEmpty()) return;
        hideKeyboard();
        addressBar.clearFocus();
        navigate(normalizeUrl(input));
    }

    private void navigate(String url) {
        Tab tab = currentTab();
        if (tab == null) {
            createTab(url, true);
            return;
        }
        tab.url = url;
        tab.isStart = isStartPage(url);
        tab.view.loadUrl(url);
        updateAddressBar();
    }

    private void openInNewTab(String input) {
        createTab(normalizeUrl(input), true);
    }

    private void goBack() {
        Tab tab = currentTab();
        if (tab != null && tab.view.canGoBack()) tab.view.goBack();
    }

    private void goForward() {
        Tab tab = currentTab();
        if (tab != null && tab.view.canGoForward()) tab.view.goForward();
    }

    private void goHome() {
        String home = store.getHomePage().trim();
        navigate(home.isEmpty() ? startPageUrl() : normalizeUrl(home));
    }

    private void updateAddressBar() {
        Tab tab = currentTab();
        if (tab == null || addressBar.hasFocus()) return;
        addressBar.setText(tab.isStart ? "" : tab.url);
        addressBar.setHint("Поиск или адрес");
        boolean secure = tab.url != null && tab.url.toLowerCase().startsWith("https://");
        securityIcon.setImageResource(tab.isStart ? R.drawable.ic_search : (secure ? R.drawable.ic_lock : R.drawable.ic_globe));
        reloadButton.setImageResource(tab.isStart ? R.drawable.ic_search : R.drawable.ic_refresh);
    }

    private void hideKeyboard() {
        InputMethodManager imm = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
        View focus = getCurrentFocus();
        if (imm != null && focus != null) {
            imm.hideSoftInputFromWindow(focus.getWindowToken(), 0);
        }
    }

    // ---------------------------------------------------------------- find in page

    private void showFindBar() {
        findBar.setVisibility(View.VISIBLE);
        findInput.requestFocus();
        InputMethodManager imm = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) imm.showSoftInput(findInput, 0);
        String query = findInput.getText().toString().trim();
        if (!query.isEmpty()) runFind(true);
    }

    private void hideFindBar() {
        if (findBar == null) return;
        findBar.setVisibility(View.GONE);
        Tab tab = currentTab();
        if (tab != null) tab.view.clearMatches();
        hideKeyboard();
    }

    private void runFind(boolean forward) {
        Tab tab = currentTab();
        if (tab == null) return;
        String query = findInput.getText().toString().trim();
        if (query.isEmpty()) {
            tab.view.clearMatches();
            findStatus.setText("");
            return;
        }
        tab.view.findAllAsync(query);
        tab.view.findNext(forward);
    }

    @Override
    public void onFindResultReceived(int activeMatchOrdinal, int numberOfMatches, boolean isDoneCounting) {
        if (findStatus == null) return;
        if (numberOfMatches == 0) {
            findStatus.setText("0/0");
        } else {
            findStatus.setText((activeMatchOrdinal + 1) + "/" + numberOfMatches);
        }
    }

    // ---------------------------------------------------------------- web client

    private final class NovaWebViewClient extends WebViewClient {
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            if (request == null) return false;
            return handleUrl(request.getUrl() == null ? "" : request.getUrl().toString());
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, String url) {
            return handleUrl(url);
        }

        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            Tab tab = tabFor(view);
            if (tab != null) {
                tab.url = url;
                tab.isStart = isStartPage(url);
            }
            progressBar.setVisibility(View.VISIBLE);
            progressBar.setProgress(5);
            if (tab == currentTab()) updateAddressBar();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            Tab tab = tabFor(view);
            if (tab != null) {
                tab.url = url;
                tab.isStart = isStartPage(url);
                if (TextUtils.isEmpty(tab.title)) tab.title = "Nova Browser";
                store.addHistory(tab.title, url);
            }
            progressBar.setVisibility(View.GONE);
            if (tab == currentTab()) {
                updateAddressBar();
                updateNavButtons();
            }
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (request == null || !request.isForMainFrame()) return;
            String message = error == null ? "Сеть недоступна" : String.valueOf(error.getDescription());
            showErrorPage(view, message);
        }

        @Override
        public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
            if (failingUrl == null) return;
            Tab tab = tabFor(view);
            if (tab != null && !failingUrl.equals(tab.url)) return;
            showErrorPage(view, description);
        }
    }

    private boolean handleUrl(String url) {
        if (url == null) return false;
        if (url.startsWith("nova://")) {
            String path = url.substring(7);
            if (path.startsWith("/settings") || path.equals("settings")) { showSettings(); return true; }
            if (path.startsWith("/bookmarks") || path.equals("bookmarks")) { showBookmarks(); return true; }
            if (path.startsWith("/history") || path.equals("history")) { showHistory(); return true; }
            if (path.startsWith("/downloads") || path.equals("downloads")) { showDownloads(); return true; }
            if (path.startsWith("/newtab") || path.equals("newtab")) { openInNewTab(startPageUrl()); return true; }
            if (path.startsWith("/home") || path.equals("home")) { goHome(); return true; }
            return true;
        }
        if (url.startsWith("mailto:") || url.startsWith("tel:") || url.startsWith("sms:")) {
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
            } catch (Exception ignored) {
            }
            return true;
        }
        return false;
    }

    private void showErrorPage(WebView view, String description) {
        String safe = description == null ? "" : description.replace("<", "").replace(">", "");
        String html = "<!DOCTYPE html><html><head><meta name='viewport' content='width=device-width, initial-scale=1'>"
                + "<style>body{margin:0;min-height:100vh;display:flex;flex-direction:column;align-items:center;"
                + "justify-content:center;background:" + (palette.dark ? "#0b1122" : "#f4f7ff") + ";color:"
                + (palette.dark ? "#f4f7ff" : "#111827") + ";font-family:Roboto,sans-serif;padding:28px;text-align:center}"
                + "h2{font-size:20px;margin:0 0 10px}p{font-size:14px;opacity:.7;margin:0 0 22px}"
                + "button{border:0;border-radius:14px;padding:13px 26px;font-size:15px;font-weight:600;"
                + "background:" + (palette.dark ? "#7f9aff" : "#5b6cff") + ";color:#fff}</style></head><body>"
                + "<h2>Страница не открылась</h2><p>" + safe + "</p>"
                + "<button onclick='location.reload()'>Повторить</button></body></html>";
        view.loadDataWithBaseURL("http://nova.local/error", html, "text/html", "utf-8", null);
    }

    private final class NovaChromeClient extends WebChromeClient {
        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            progressBar.setVisibility(newProgress >= 100 ? View.GONE : View.VISIBLE);
            progressBar.setProgress(newProgress);
        }

        @Override
        public void onReceivedTitle(WebView view, String title) {
            Tab tab = tabFor(view);
            if (tab == null) return;
            tab.title = TextUtils.isEmpty(title) ? "Nova Browser" : title;
        }

        @Override
        public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> callback,
                                         FileChooserParams fileChooserParams) {
            if (filePathCallback != null) {
                filePathCallback.onReceiveValue(null);
            }
            filePathCallback = callback;
            try {
                Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                startActivityForResult(Intent.createChooser(intent, "Выберите файл"), REQUEST_FILE_CHOOSER);
            } catch (Exception e) {
                filePathCallback = null;
                return false;
            }
            return true;
        }

        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            if (fullscreenView == null) return;
            fullscreenView.removeAllViews();
            fullscreenView.addView(view, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            fullscreenView.setVisibility(View.VISIBLE);
            Window window = getWindow();
            if (window != null) window.addFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);
        }

        @Override
        public void onHideCustomView() {
            if (fullscreenView == null) return;
            fullscreenView.removeAllViews();
            fullscreenView.setVisibility(View.GONE);
            Window window = getWindow();
            if (window != null) window.clearFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN);
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == REQUEST_FILE_CHOOSER) {
            if (filePathCallback == null) return;
            Uri[] result = null;
            if (resultCode == RESULT_OK && data != null) {
                if (data.getClipData() != null) {
                    int count = data.getClipData().getItemCount();
                    result = new Uri[count];
                    for (int i = 0; i < count; i++) {
                        result[i] = data.getClipData().getItemAt(i).getUri();
                    }
                } else if (data.getData() != null) {
                    result = new Uri[]{data.getData()};
                }
            }
            filePathCallback.onReceiveValue(result);
            filePathCallback = null;
            return;
        }
        super.onActivityResult(requestCode, resultCode, data);
    }

    // ---------------------------------------------------------------- downloads

    @Override
    public void onDownloadStart(String url, String userAgent, String contentDisposition,
                                String mimeType, long contentLength) {
        try {
            String fileName = URLUtil.guessFileName(url, contentDisposition, mimeType);
            DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
            request.setMimeType(mimeType);
            request.addRequestHeader("User-Agent", userAgent);
            request.setTitle(fileName);
            request.setDescription("Nova Browser");
            request.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
            request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, fileName);
            DownloadManager manager = (DownloadManager) getSystemService(Context.DOWNLOAD_SERVICE);
            if (manager == null) {
                toast("Загрузка недоступна");
                return;
            }
            manager.enqueue(request);
            toast("Загрузка началась: " + fileName);
        } catch (Exception e) {
            toast("Не удалось скачать файл");
        }
    }

    // ---------------------------------------------------------------- dialogs

    private void dismissSheet() {
        if (activeDialog != null && activeDialog.isShowing()) activeDialog.dismiss();
        activeDialog = null;
    }

    private void showSheet(View content) {
        dismissSheet();
        final Dialog dialog = new Dialog(this, android.R.style.Theme_Translucent_NoTitleBar);
        FrameLayout wrapper = new FrameLayout(this);
        wrapper.setPadding(dp(10), dp(10), dp(10), dp(10));
        wrapper.setOnClickListener(v -> dialog.dismiss());
        content.setClickable(true);
        wrapper.addView(content, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM));
        dialog.setContentView(wrapper);
        activeDialog = dialog;
        dialog.show();
        Window window = dialog.getWindow();
        if (window != null) {
            window.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));
            window.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
            window.setGravity(Gravity.BOTTOM);
            window.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);
            window.setDimAmount(0.5f);
        }
    }

    private LinearLayout card() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setBackground(rounded(palette.surface, dp(20)));
        card.setPadding(dp(14), dp(10), dp(14), dp(12));
        card.setElevation(dp(16));
        return card;
    }

    private void addHeader(LinearLayout card, String title) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(4), dp(4), dp(0), dp(6));
        TextView label = new TextView(this);
        label.setText(title);
        label.setTextSize(17);
        label.setTypeface(Typeface.DEFAULT_BOLD);
        label.setTextColor(palette.text);
        label.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        row.addView(label);
        ImageButton close = new ImageButton(this);
        close.setBackground(null);
        close.setImageResource(R.drawable.ic_close);
        close.setColorFilter(palette.muted);
        close.setContentDescription("Закрыть");
        close.setLayoutParams(new LinearLayout.LayoutParams(dp(32), dp(32)));
        close.setOnClickListener(v -> dismissSheet());
        row.addView(close);
        card.addView(row);
    }

    private void addDivider(LinearLayout card) {
        View line = new View(this);
        line.setBackgroundColor(palette.line);
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(1));
        params.topMargin = dp(6);
        params.bottomMargin = dp(6);
        card.addView(line, params);
    }

    private View rowItem(int iconRes, String title, String detail, int tint, Runnable action) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(6), dp(9), dp(6), dp(9));

        ImageView icon = new ImageView(this);
        icon.setImageResource(iconRes);
        icon.setColorFilter(tint);
        LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(dp(21), dp(21));
        iconParams.rightMargin = dp(14);
        icon.setLayoutParams(iconParams);
        row.addView(icon);

        LinearLayout texts = new LinearLayout(this);
        texts.setOrientation(LinearLayout.VERTICAL);
        texts.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        TextView label = new TextView(this);
        label.setText(title);
        label.setTextSize(15);
        label.setTextColor(palette.text);
        texts.addView(label);
        if (detail != null && !detail.isEmpty()) {
            TextView sub = new TextView(this);
            sub.setText(detail);
            sub.setTextSize(11);
            sub.setTextColor(palette.muted);
            sub.setSingleLine(true);
            sub.setEllipsize(TextUtils.TruncateAt.END);
            texts.addView(sub);
        }
        row.addView(texts);

        row.setOnClickListener(v -> {
            dismissSheet();
            action.run();
        });
        return row;
    }

    private View sectionLabel(String text) {
        TextView label = new TextView(this);
        label.setText(text);
        label.setTextSize(11);
        label.setTypeface(Typeface.DEFAULT_BOLD);
        label.setTextColor(palette.muted);
        label.setPadding(dp(4), dp(12), dp(4), dp(4));
        return label;
    }

    private ScrollView scrollBox(int heightDp) {
        ScrollView scroll = new ScrollView(this);
        scroll.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(heightDp)));
        return scroll;
    }

    private int sheetHeight(int desiredDp) {
        int screen = getResources().getDisplayMetrics().heightPixels;
        return Math.min(dp(desiredDp), (int) (screen * 0.62f));
    }

    private void showMenu() {
        LinearLayout card = card();
        addHeader(card, "Nova Browser");

        Tab tab = currentTab();
        String url = tab == null ? "" : tab.url;
        boolean bookmarked = store.isBookmarked(url);

        ScrollView scroll = new ScrollView(this);
        scroll.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, sheetHeight(430)));
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        scroll.addView(list);

        list.addView(rowItem(R.drawable.ic_add, "Новая вкладка", null, palette.accent,
                () -> openInNewTab(startPageUrl())));
        list.addView(rowItem(bookmarked ? R.drawable.ic_star_filled : R.drawable.ic_star,
                bookmarked ? "Убрать из закладок" : "В закладки",
                bookmarked ? null : url, palette.accent,
                () -> toggleBookmark()));
        list.addView(rowItem(R.drawable.ic_star_filled, "Закладки", null, palette.accent,
                this::showBookmarks));
        list.addView(rowItem(R.drawable.ic_history, "История", null, palette.accent,
                this::showHistory));
        list.addView(rowItem(R.drawable.ic_download, "Загрузки", null, palette.accent,
                this::showDownloads));
        list.addView(rowItem(R.drawable.ic_search, "Найти на странице", null, palette.accent,
                this::showFindBar));
        list.addView(rowItem(R.drawable.ic_share, "Поделиться", null, palette.accent,
                this::sharePage));
        list.addView(rowItem(R.drawable.ic_refresh, "Обновить", null, palette.accent,
                () -> {
                    Tab t = currentTab();
                    if (t != null) t.view.reload();
                }));
        list.addView(rowItem(R.drawable.ic_settings, "Настройки", null, palette.accent,
                this::showSettings));
        list.addView(rowItem(R.drawable.ic_info, "О браузере", "Nova Browser 1.0.0", palette.accent,
                this::showAbout));

        card.addView(scroll);
        showSheet(card);
    }

    private void toggleBookmark() {
        Tab tab = currentTab();
        if (tab == null || TextUtils.isEmpty(tab.url) || tab.isStart) {
            toast("Эту страницу нельзя добавить в закладки");
            return;
        }
        if (store.isBookmarked(tab.url)) {
            store.removeBookmark(tab.url);
            toast("Удалено из закладок");
        } else {
            store.addBookmark(tab.title, tab.url);
            toast("Добавлено в закладки");
        }
    }

    private void sharePage() {
        Tab tab = currentTab();
        if (tab == null || TextUtils.isEmpty(tab.url) || tab.isStart) {
            toast("Нечего отправить");
            return;
        }
        Intent intent = new Intent(Intent.ACTION_SEND);
        intent.setType("text/plain");
        intent.putExtra(Intent.EXTRA_SUBJECT, tab.title);
        intent.putExtra(Intent.EXTRA_TEXT, tab.url);
        try {
            startActivity(Intent.createChooser(intent, "Поделиться"));
        } catch (Exception e) {
            toast("Нет приложений для отправки");
        }
    }

    private void showTabs() {
        LinearLayout card = card();
        addHeader(card, "Вкладки (" + tabs.size() + ")");

        TextView newTab = new TextView(this);
        newTab.setText("+  Новая вкладка");
        newTab.setTextSize(14);
        newTab.setGravity(Gravity.CENTER);
        newTab.setPadding(0, dp(10), 0, dp(10));
        newTab.setTextColor(Color.WHITE);
        newTab.setBackground(rounded(palette.accent, dp(12)));
        newTab.setOnClickListener(v -> {
            dismissSheet();
            openInNewTab(startPageUrl());
        });
        card.addView(newTab, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        ScrollView scroll = scrollBox(360);
        scroll.getLayoutParams().height = sheetHeight(360);
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        list.setPadding(0, dp(8), 0, 0);
        scroll.addView(list);

        for (int i = 0; i < tabs.size(); i++) {
            final int index = i;
            Tab tab = tabs.get(i);
            boolean active = i == selectedIndex;

            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.setPadding(dp(10), dp(8), dp(4), dp(8));
            row.setBackground(rounded(active ? palette.accentSoft : palette.surface2, dp(12)));

            LinearLayout texts = new LinearLayout(this);
            texts.setOrientation(LinearLayout.VERTICAL);
            texts.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

            TextView title = new TextView(this);
            title.setText(tab.title == null ? "Nova Browser" : tab.title);
            title.setTextSize(14);
            title.setSingleLine(true);
            title.setEllipsize(TextUtils.TruncateAt.END);
            title.setTextColor(palette.text);
            texts.addView(title);

            TextView sub = new TextView(this);
            sub.setText(tab.isStart ? "Новая вкладка" : tab.url);
            sub.setTextSize(11);
            sub.setSingleLine(true);
            sub.setEllipsize(TextUtils.TruncateAt.END);
            sub.setTextColor(palette.muted);
            texts.addView(sub);

            row.addView(texts);

            ImageButton close = new ImageButton(this);
            close.setBackground(null);
            close.setImageResource(R.drawable.ic_close);
            close.setColorFilter(palette.muted);
            close.setContentDescription("Закрыть вкладку");
            close.setLayoutParams(new LinearLayout.LayoutParams(dp(34), dp(34)));
            close.setOnClickListener(v -> {
                dismissSheet();
                closeTab(index);
            });
            row.addView(close);

            row.setOnClickListener(v -> {
                dismissSheet();
                selectTab(index);
            });

            LinearLayout.LayoutParams rowParams = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            rowParams.bottomMargin = dp(6);
            list.addView(row, rowParams);
        }

        card.addView(scroll);
        showSheet(card);
    }

    private void showBookmarks() {
        LinearLayout card = card();
        addHeader(card, "Закладки");
        List<NovaStore.Entry> entries = store.getBookmarks();

        if (entries.isEmpty()) {
            card.addView(emptyLabel("Закладок пока нет"));
        } else {
            ScrollView scroll = scrollBox(380);
            scroll.getLayoutParams().height = sheetHeight(380);
            LinearLayout list = new LinearLayout(this);
            list.setOrientation(LinearLayout.VERTICAL);
            scroll.addView(list);
            for (NovaStore.Entry entry : entries) {
                list.addView(entryRow(entry, true));
            }
            card.addView(scroll);
        }
        showSheet(card);
    }

    private void showHistory() {
        LinearLayout card = card();
        addHeader(card, "История");
        List<NovaStore.Entry> entries = store.getHistory();

        if (entries.isEmpty()) {
            card.addView(emptyLabel("История пуста"));
        } else {
            TextView clear = new TextView(this);
            clear.setText("Очистить историю");
            clear.setTextSize(13);
            clear.setGravity(Gravity.CENTER);
            clear.setPadding(0, dp(8), 0, dp(8));
            clear.setTextColor(palette.danger);
            clear.setOnClickListener(v -> {
                dismissSheet();
                store.clearHistory();
                toast("История очищена");
            });
            card.addView(clear, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

            ScrollView scroll = scrollBox(380);
            scroll.getLayoutParams().height = sheetHeight(380);
            LinearLayout list = new LinearLayout(this);
            list.setOrientation(LinearLayout.VERTICAL);
            scroll.addView(list);
            for (NovaStore.Entry entry : entries) {
                list.addView(entryRow(entry, false));
            }
            card.addView(scroll);
        }
        showSheet(card);
    }

    private View entryRow(NovaStore.Entry entry, boolean deletable) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(10), dp(8), dp(4), dp(8));
        row.setBackground(rounded(palette.surface2, dp(12)));

        LinearLayout texts = new LinearLayout(this);
        texts.setOrientation(LinearLayout.VERTICAL);
        texts.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView title = new TextView(this);
        title.setText(TextUtils.isEmpty(entry.title) ? entry.url : entry.title);
        title.setTextSize(14);
        title.setSingleLine(true);
        title.setEllipsize(TextUtils.TruncateAt.END);
        title.setTextColor(palette.text);
        texts.addView(title);

        TextView url = new TextView(this);
        url.setText(entry.url);
        url.setTextSize(11);
        url.setSingleLine(true);
        url.setEllipsize(TextUtils.TruncateAt.END);
        url.setTextColor(palette.muted);
        texts.addView(url);

        row.addView(texts);

        ImageButton open = new ImageButton(this);
        open.setBackground(null);
        open.setImageResource(R.drawable.ic_open);
        open.setColorFilter(palette.accent);
        open.setContentDescription("Открыть");
        open.setLayoutParams(new LinearLayout.LayoutParams(dp(34), dp(34)));
        open.setOnClickListener(v -> {
            dismissSheet();
            navigate(entry.url);
        });
        row.addView(open);

        if (deletable) {
            ImageButton delete = new ImageButton(this);
            delete.setBackground(null);
            delete.setImageResource(R.drawable.ic_delete);
            delete.setColorFilter(palette.danger);
            delete.setContentDescription("Удалить");
            delete.setLayoutParams(new LinearLayout.LayoutParams(dp(34), dp(34)));
            delete.setOnClickListener(v -> {
                dismissSheet();
                store.removeBookmark(entry.url);
                showBookmarks();
            });
            row.addView(delete);
        }

        row.setOnClickListener(v -> {
            dismissSheet();
            navigate(entry.url);
        });

        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.bottomMargin = dp(6);
        row.setLayoutParams(params);
        return row;
    }

    private TextView emptyLabel(String text) {
        TextView empty = new TextView(this);
        empty.setText(text);
        empty.setTextSize(14);
        empty.setGravity(Gravity.CENTER);
        empty.setTextColor(palette.muted);
        empty.setPadding(0, dp(40), 0, dp(40));
        return empty;
    }

    private void showDownloads() {
        LinearLayout card = card();
        addHeader(card, "Загрузки");

        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);

        int count = 0;
        DownloadManager manager = (DownloadManager) getSystemService(Context.DOWNLOAD_SERVICE);
        Cursor cursor = null;
        if (manager != null) {
            try {
                cursor = manager.query(new DownloadManager.Query());
                if (cursor != null) {
                    while (cursor.moveToNext() && count < 40) {
                        String title = cursor.getString(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_TITLE));
                        String localUri = cursor.getString(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_LOCAL_URI));
                        String mime = cursor.getString(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_MEDIA_TYPE));
                        int status = cursor.getInt(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_STATUS));
                        long done = cursor.getLong(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_BYTES_DOWNLOADED_SO_FAR));
                        long total = cursor.getLong(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_TOTAL_SIZE_BYTES));

                        String detail = downloadStatus(status);
                        if (total > 0) detail = detail + " · " + Formatter.formatFileSize(this, done)
                                + " из " + Formatter.formatFileSize(this, total);

                        final String openUri = localUri;
                        final String openMime = mime;
                        View row = rowItem(status == DownloadManager.STATUS_SUCCESSFUL
                                        ? R.drawable.ic_open : R.drawable.ic_download,
                                TextUtils.isEmpty(title) ? "Файл" : title, detail, palette.accent,
                                () -> openDownloaded(openUri, openMime));
                        list.addView(row);
                        count++;
                    }
                }
            } catch (Exception ignored) {
            } finally {
                if (cursor != null) cursor.close();
            }
        }

        if (count == 0) {
            card.addView(emptyLabel("Загрузок пока нет"));
        } else {
            ScrollView scroll = scrollBox(380);
            scroll.getLayoutParams().height = sheetHeight(380);
            scroll.addView(list);
            card.addView(scroll);
        }
        showSheet(card);
    }

    private String downloadStatus(int status) {
        switch (status) {
            case DownloadManager.STATUS_SUCCESSFUL: return "Завершено";
            case DownloadManager.STATUS_RUNNING: return "Скачивается";
            case DownloadManager.STATUS_PAUSED: return "Приостановлено";
            case DownloadManager.STATUS_PENDING: return "В очереди";
            case DownloadManager.STATUS_FAILED: return "Ошибка";
            default: return "";
        }
    }

    private void openDownloaded(String localUri, String mime) {
        if (TextUtils.isEmpty(localUri)) {
            toast("Файл недоступен");
            return;
        }
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(Uri.parse(localUri), TextUtils.isEmpty(mime) ? "*/*" : mime);
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            startActivity(intent);
        } catch (Exception e) {
            toast("Нет приложения для этого файла");
        }
    }

    private void showSettings() {
        LinearLayout card = card();
        addHeader(card, "Настройки");

        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        ScrollView scroll = new ScrollView(this);
        scroll.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, sheetHeight(520)));
        scroll.addView(body);

        body.addView(sectionLabel("ТЕМА"));
        String mode = store.getThemeMode();
        body.addView(chipRow(new String[]{"Светлая", "Тёмная", "Системная"},
                new String[]{"light", "dark", "system"}, mode, value -> {
                    store.setThemeMode(value);
                    dismissSheet();
                    reloadTheme();
                    showSettings();
                }));

        body.addView(sectionLabel("ПОИСКОВАЯ СИСТЕМА"));
        body.addView(chipRow(NovaStore.ENGINES, NovaStore.ENGINES, store.getSearchEngine(), value -> {
            store.setSearchEngine(value);
            dismissSheet();
            showSettings();
        }));

        body.addView(sectionLabel("ДОМАШНЯЯ СТРАНИЦА"));
        EditText home = new EditText(this);
        home.setText(store.getHomePage());
        home.setHint("Пусто — стартовая страница Nova");
        home.setSingleLine(true);
        home.setTextSize(14);
        home.setTextColor(palette.text);
        home.setHintTextColor(palette.muted);
        home.setBackground(rounded(palette.surface2, dp(12)));
        home.setPadding(dp(12), dp(8), dp(12), dp(8));
        body.addView(home, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView save = new TextView(this);
        save.setText("Сохранить");
        save.setTextSize(14);
        save.setGravity(Gravity.CENTER);
        save.setPadding(0, dp(10), 0, dp(10));
        save.setTextColor(Color.WHITE);
        save.setBackground(rounded(palette.accent, dp(12)));
        save.setOnClickListener(v -> {
            store.setHomePage(home.getText().toString().trim());
            toast("Домашняя страница сохранена");
        });
        LinearLayout.LayoutParams saveParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        saveParams.topMargin = dp(8);
        body.addView(save, saveParams);

        body.addView(sectionLabel("ДАННЫЕ"));
        body.addView(rowItem(R.drawable.ic_history, "Очистить историю", null, palette.danger, () -> {
            store.clearHistory();
            toast("История очищена");
        }));
        body.addView(rowItem(R.drawable.ic_star, "Очистить закладки", null, palette.danger, () -> {
            store.clearBookmarks();
            toast("Закладки удалены");
        }));

        body.addView(sectionLabel("О ПРИЛОЖЕНИИ"));
        TextView about = new TextView(this);
        about.setText("Nova Browser 1.0.0\nБыстрый, приватный браузер для Android.");
        about.setTextSize(12);
        about.setTextColor(palette.muted);
        about.setPadding(dp(4), dp(4), dp(4), dp(12));
        body.addView(about);

        card.addView(scroll);
        showSheet(card);
    }

    private View chipRow(String[] labels, String[] values, String current, ValueAction action) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(0, dp(4), 0, dp(4));
        for (int i = 0; i < labels.length && i < values.length; i++) {
            final String value = values[i];
            boolean active = value.equals(current);
            TextView chip = new TextView(this);
            chip.setText(labels[i]);
            chip.setTextSize(12);
            chip.setGravity(Gravity.CENTER);
            chip.setPadding(dp(4), dp(9), dp(4), dp(9));
            chip.setTextColor(active ? Color.WHITE : palette.text);
            chip.setBackground(rounded(active ? palette.accent : palette.surface2, dp(12)));
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0,
                    ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            params.rightMargin = i < labels.length - 1 ? dp(6) : 0;
            chip.setLayoutParams(params);
            chip.setOnClickListener(v -> action.run(value));
            row.addView(chip);
        }
        return row;
    }

    private void showAbout() {
        LinearLayout card = card();
        addHeader(card, "О браузере");

        ImageView logo = new ImageView(this);
        logo.setImageResource(R.mipmap.ic_launcher);
        logo.setLayoutParams(new LinearLayout.LayoutParams(dp(64), dp(64)));
        card.addView(logo);

        TextView title = new TextView(this);
        title.setText("Nova Browser 1.0.0");
        title.setTextSize(17);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextColor(palette.text);
        title.setPadding(0, dp(10), 0, dp(4));
        card.addView(title);

        TextView body = new TextView(this);
        body.setText("Быстрый и приватный браузер.\n\n"
                + "• Вкладки, закладки и история\n"
                + "• Поиск по странице\n"
                + "• Загрузки\n"
                + "• Тёмная и светлая темы\n\n"
                + "Движок: системный Android WebView (Chromium).");
        body.setTextSize(13);
        body.setTextColor(palette.muted);
        body.setLineSpacing(dp(3), 1f);
        body.setPadding(0, 0, 0, dp(6));
        card.addView(body);

        showSheet(card);
    }

    // ---------------------------------------------------------------- bridge

    private final class NovaBridge {
        @JavascriptInterface
        public void search(String query) {
            runOnUiThread(() -> navigate(store.searchUrl(query)));
        }

        @JavascriptInterface
        public void openUrl(String url) {
            runOnUiThread(() -> navigate(normalizeUrl(url)));
        }

        @JavascriptInterface
        public String getTheme() {
            return palette.dark ? "dark" : "light";
        }

        @JavascriptInterface
        public String getBookmarks() {
            return store.getBookmarksJson();
        }

        @JavascriptInterface
        public String getHistory() {
            return store.getHistoryJson();
        }

        @JavascriptInterface
        public String getSearchEngine() {
            return store.getSearchEngine();
        }

        @JavascriptInterface
        public void openSettings() {
            runOnUiThread(MainActivity.this::showSettings);
        }

        @JavascriptInterface
        public void openBookmarks() {
            runOnUiThread(MainActivity.this::showBookmarks);
        }

        @JavascriptInterface
        public void openHistory() {
            runOnUiThread(MainActivity.this::showHistory);
        }

        @JavascriptInterface
        public void openDownloads() {
            runOnUiThread(MainActivity.this::showDownloads);
        }

        @JavascriptInterface
        public void reload() {
            runOnUiThread(() -> {
                Tab tab = currentTab();
                if (tab != null) tab.view.reload();
            });
        }

        @JavascriptInterface
        public void openHome() {
            runOnUiThread(MainActivity.this::goHome);
        }
    }

    // ---------------------------------------------------------------- lifecycle

    @Override
    public void onBackPressed() {
        if (fullscreenView != null && fullscreenView.getVisibility() == View.VISIBLE) {
            fullscreenView.removeAllViews();
            fullscreenView.setVisibility(View.GONE);
            return;
        }
        if (findBar != null && findBar.getVisibility() == View.VISIBLE) {
            hideFindBar();
            return;
        }
        if (activeDialog != null && activeDialog.isShowing()) {
            dismissSheet();
            return;
        }
        Tab tab = currentTab();
        if (tab != null && tab.view.canGoBack()) {
            tab.view.goBack();
            return;
        }
        if (tabs.size() > 1) {
            closeTab(selectedIndex);
            return;
        }
        super.onBackPressed();
    }

    @Override
    protected void onPause() {
        super.onPause();
        for (Tab tab : tabs) {
            tab.view.onPause();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        for (Tab tab : tabs) {
            tab.view.onResume();
        }
    }

    @Override
    protected void onDestroy() {
        dismissSheet();
        for (Tab tab : tabs) {
            try {
                tab.view.destroy();
            } catch (Exception ignored) {
            }
        }
        tabs.clear();
        super.onDestroy();
    }

    // ---------------------------------------------------------------- utils

    private int dp(float value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private GradientDrawable rounded(int color, float radius) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(color);
        drawable.setCornerRadius(radius);
        return drawable;
    }

    private void toast(String text) {
        Toast.makeText(this, text, Toast.LENGTH_SHORT).show();
    }
}
