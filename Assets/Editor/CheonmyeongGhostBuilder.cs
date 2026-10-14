// 천명 · 원귀 3종 애니메이션 자동 생성 (에디터 전용)
// 메뉴: 천명 > 원귀 애니메이션 만들기
// 하는 일
//  1) Assets/Char/Ghost-Sheet.png 를 64px 9칸으로 자르기 (몽달귀 · 달걀귀신 · 손각시 × 외형 3)
//  2) 걷기(종류별) · 피격 · 타격 애니메이션 클립 만들기 (그림 1장을 곡선으로 움직임)
//  3) 종류별 애니메이터 컨트롤러 + 원귀 9종 프리팹 만들기
// 프리팹 구조: Ghost_XXX (Animator) └ Sprite (SpriteRenderer)  ← 애니메이션은 자식 "Sprite"만 움직이므로 이동 코드와 겹치지 않음
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class CheonmyeongGhostBuilder
{
    const string SheetPath = "Assets/Char/Ghost-Sheet.png";
    const string Root = "Assets/Char/Ghosts";
    const string PrefabDir = "Assets/Char/Ghosts/Prefabs";
    const int Cell = 64;
    static readonly string[] Types = { "Mongdal", "Egg", "Songaksi" };   // 몽달귀, 달걀귀신, 손각시 (시트 위에서부터)

    [MenuItem("천명/원귀 애니메이션 만들기")]
    public static void Build()
    {
        SliceSheet();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToDictionary(s => s.name);
        EnsureFolder("Assets/Char", "Ghosts");
        EnsureFolder(Root, "Prefabs");

        var hit = SaveClip(MakeHit(), "Ghost_Hit");
        var attack = SaveClip(MakeAttack(), "Ghost_Attack");
        var walks = new Dictionary<string, AnimationClip>
        {
            { "Mongdal", SaveClip(MakeWalkMongdal(), "Ghost_Walk_Mongdal") },
            { "Egg", SaveClip(MakeWalkEgg(), "Ghost_Walk_Egg") },
            { "Songaksi", SaveClip(MakeWalkSongaksi(), "Ghost_Walk_Songaksi") },
        };

        foreach (var type in Types)
        {
            var ctrl = MakeController(type, walks[type], hit, attack);
            for (int v = 0; v < 3; v++)
            {
                string spriteName = $"{type}_{v}";
                if (!sprites.TryGetValue(spriteName, out var sp)) { Debug.LogError($"[천명] 스프라이트 없음: {spriteName}"); continue; }
                MakePrefab($"Ghost_{type}_{v}", sp, ctrl);
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[천명] 원귀 애니메이션 · 컨트롤러 3개 · 프리팹 9개 생성 완료 → " + Root);
    }

    // ---------- 1. 시트 자르기 ----------
    static void SliceSheet()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError("[천명] 시트를 찾을 수 없음: " + SheetPath); return; }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 64;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
        dp.InitSpriteEditorDataProvider();
        var rects = new List<SpriteRect>();
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                rects.Add(new SpriteRect
                {
                    name = $"{Types[row]}_{col}",
                    spriteID = GUID.Generate(),
                    rect = new Rect(col * Cell, (2 - row) * Cell, Cell, Cell),   // 유니티는 아래쪽이 y=0
                    alignment = SpriteAlignment.BottomCenter,
                    pivot = new Vector2(0.5f, 0f),
                });
        dp.SetSpriteRects(rects.ToArray());
        var names = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (names != null) names.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        dp.Apply();
        importer.SaveAndReimport();
    }

    // ---------- 2. 클립 ----------
    static AnimationCurve Curve(params float[] tv)
    {
        var c = new AnimationCurve();
        for (int i = 0; i + 1 < tv.Length; i += 2) c.AddKey(new Keyframe(tv[i], tv[i + 1]));
        for (int i = 0; i < c.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        return c;
    }
    static void Set(AnimationClip clip, System.Type type, string prop, AnimationCurve c)
        => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Sprite", type, prop), c);
    static void Pos(AnimationClip k, string axis, AnimationCurve c) => Set(k, typeof(Transform), "m_LocalPosition." + axis, c);
    static void Scl(AnimationClip k, string axis, AnimationCurve c) => Set(k, typeof(Transform), "m_LocalScale." + axis, c);
    static void Rot(AnimationClip k, AnimationCurve z)
    {
        float end = z.keys[z.length - 1].time;
        Set(k, typeof(Transform), "localEulerAnglesRaw.x", Curve(0, 0, end, 0));
        Set(k, typeof(Transform), "localEulerAnglesRaw.y", Curve(0, 0, end, 0));
        Set(k, typeof(Transform), "localEulerAnglesRaw.z", z);
    }
    static void Col(AnimationClip k, string ch, AnimationCurve c) => Set(k, typeof(SpriteRenderer), "m_Color." + ch, c);
    static AnimationClip NewClip(bool loop)
    {
        var clip = new AnimationClip { frameRate = 60 };
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        return clip;
    }

    // 몽달귀: 느리게 둥실, 좌우로 살짝 흔들림 (1.0초 반복)
    static AnimationClip MakeWalkMongdal()
    {
        var k = NewClip(true);
        Pos(k, "y", Curve(0, 0f, 0.5f, 0.06f, 1f, 0f));
        Rot(k, Curve(0, -3f, 0.5f, 3f, 1f, -3f));
        return k;
    }
    // 달걀귀신: 빠르게 통통 튀며 굴러옴, 바닥에서 찌그러짐 (0.45초 반복)
    static AnimationClip MakeWalkEgg()
    {
        var k = NewClip(true);
        Pos(k, "y", Curve(0, 0f, 0.225f, 0.12f, 0.45f, 0f));
        Scl(k, "x", Curve(0, 1.12f, 0.225f, 0.95f, 0.45f, 1.12f));
        Scl(k, "y", Curve(0, 0.88f, 0.225f, 1.05f, 0.45f, 0.88f));
        return k;
    }
    // 손각시: 천천히 떠다니며 크게 흔들리고 희미하게 깜빡임 (1.6초 반복)
    static AnimationClip MakeWalkSongaksi()
    {
        var k = NewClip(true);
        Pos(k, "y", Curve(0, 0f, 0.8f, 0.05f, 1.6f, 0f));
        Rot(k, Curve(0, -5f, 0.8f, 5f, 1.6f, -5f));
        Col(k, "a", Curve(0, 1f, 0.8f, 0.8f, 1.6f, 1f));
        return k;
    }
    // 피격: 붉게 번쩍 + 뒤로 밀림 + 찌그러짐 (0.2초)  · 흰 원귀라 빨강으로 표시
    static AnimationClip MakeHit()
    {
        var k = NewClip(false);
        Col(k, "g", Curve(0, 0.35f, 0.08f, 0.35f, 0.2f, 1f));
        Col(k, "b", Curve(0, 0.35f, 0.08f, 0.35f, 0.2f, 1f));
        Pos(k, "x", Curve(0, 0f, 0.06f, -0.12f, 0.2f, 0f));
        Scl(k, "x", Curve(0, 1f, 0.06f, 0.85f, 0.2f, 1f));
        Scl(k, "y", Curve(0, 1f, 0.06f, 1.12f, 0.2f, 1f));
        return k;
    }
    // 타격(접촉 공격): 살짝 움츠렸다가 앞으로 덮침 (0.35초)
    static AnimationClip MakeAttack()
    {
        var k = NewClip(false);
        Pos(k, "x", Curve(0, 0f, 0.1f, -0.05f, 0.2f, 0.18f, 0.35f, 0f));
        Scl(k, "x", Curve(0, 1f, 0.1f, 0.95f, 0.2f, 1.15f, 0.35f, 1f));
        Scl(k, "y", Curve(0, 1f, 0.1f, 0.95f, 0.2f, 1.15f, 0.35f, 1f));
        Rot(k, Curve(0, 0f, 0.2f, -8f, 0.35f, 0f));
        return k;
    }
    static AnimationClip SaveClip(AnimationClip clip, string name)
    {
        string path = $"{Root}/{name}.anim";
        var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (old != null) { EditorUtility.CopySerialized(clip, old); old.name = name; EditorUtility.SetDirty(old); return old; }
        clip.name = name;
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    // ---------- 3. 컨트롤러 · 프리팹 ----------
    static AnimatorController MakeController(string type, AnimationClip walk, AnimationClip hit, AnimationClip attack)
    {
        string path = $"{Root}/Ghost_{type}.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        var sm = ctrl.layers[0].stateMachine;
        var sWalk = sm.AddState("Walk", new Vector3(300, 0, 0)); sWalk.motion = walk;
        var sHit = sm.AddState("Hit", new Vector3(560, -80, 0)); sHit.motion = hit;
        var sAtk = sm.AddState("Attack", new Vector3(560, 80, 0)); sAtk.motion = attack;
        sm.defaultState = sWalk;

        var toHit = sm.AddAnyStateTransition(sHit);
        toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        toHit.hasExitTime = false; toHit.duration = 0; toHit.canTransitionToSelf = true;
        var toAtk = sm.AddAnyStateTransition(sAtk);
        toAtk.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        toAtk.hasExitTime = false; toAtk.duration = 0; toAtk.canTransitionToSelf = false;
        foreach (var s in new[] { sHit, sAtk })
        {
            var back = s.AddTransition(sWalk);
            back.hasExitTime = true; back.exitTime = 1f; back.duration = 0;
        }
        return ctrl;
    }

    static void MakePrefab(string name, Sprite sprite, RuntimeAnimatorController ctrl)
    {
        var root = new GameObject(name);
        var anim = root.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        var child = new GameObject("Sprite");
        child.transform.SetParent(root.transform, false);
        var sr = child.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }
}
