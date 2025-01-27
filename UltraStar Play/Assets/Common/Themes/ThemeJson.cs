﻿using System;
using UnityEngine;

/**
 * Data structure for theme files.
 * The fields correspond to the possible properties defined in a JSON theme file.
 */
[Serializable]
public class ThemeJson
{
    public DynamicBackgroundJson dynamicBackground;
    public SongRatingIconsJson songRatingIcons;
    public Color32 buttonMainColor;
    public Color32 fontColorButtons;
    public Color32 fontColorLabels;
    public Color32 fontColor;

    /**
     * Time with unit for scene transition, e.g. "0.5s" or "200 ms".
     */
    public string sceneTransitionAnimationTime;
}
