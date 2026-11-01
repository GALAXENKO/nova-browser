package com.novabrowser.mobile;

import android.graphics.Color;

public final class NovaPalette {
    public final boolean dark;
    public final int bg;
    public final int bg2;
    public final int surface;
    public final int surface2;
    public final int text;
    public final int muted;
    public final int line;
    public final int accent;
    public final int accent2;
    public final int accentSoft;
    public final int danger;
    public final int address;
    public final int webBg;
    public final int nav;
    public final int shadow;

    public NovaPalette(boolean dark) {
        this.dark = dark;
        if (dark) {
            bg = Color.parseColor("#0B1122");
            bg2 = Color.parseColor("#101A33");
            surface = Color.parseColor("#141C31");
            surface2 = Color.parseColor("#1B2540");
            text = Color.parseColor("#F4F7FF");
            muted = Color.parseColor("#AEB9D4");
            line = Color.parseColor("#26314D");
            accent = Color.parseColor("#7F9AFF");
            accent2 = Color.parseColor("#B388FF");
            accentSoft = Color.parseColor("#227F9AFF");
            danger = Color.parseColor("#FF6B81");
            address = Color.parseColor("#18213A");
            webBg = Color.parseColor("#0B1122");
            nav = Color.parseColor("#10182C");
            shadow = Color.parseColor("#66000000");
        } else {
            bg = Color.parseColor("#F4F7FF");
            bg2 = Color.parseColor("#E9EFFF");
            surface = Color.parseColor("#FFFFFF");
            surface2 = Color.parseColor("#EDF2FC");
            text = Color.parseColor("#111827");
            muted = Color.parseColor("#667085");
            line = Color.parseColor("#DDE4F2");
            accent = Color.parseColor("#5B6CFF");
            accent2 = Color.parseColor("#8B5CF6");
            accentSoft = Color.parseColor("#145B6CFF");
            danger = Color.parseColor("#D92D20");
            address = Color.parseColor("#FFFFFF");
            webBg = Color.parseColor("#FFFFFF");
            nav = Color.parseColor("#FFFFFF");
            shadow = Color.parseColor("#1A223A70");
        }
    }
}
