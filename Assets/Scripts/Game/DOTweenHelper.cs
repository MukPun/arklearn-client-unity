using DG.Tweening;
using UnityEngine;

namespace Scripts.Game {
    /// <summary>
    /// DOTween 扩展方法包装器，供 Lua 脚本通过 CSharpCallLua 调用
    /// </summary>
    public static class DOTweenHelper {
        public static Tweener DOFade(CanvasGroup target, float endValue, float duration) {
            return target.DOFade(endValue, duration);
        }

        public static Tweener DOColor(SpriteRenderer target, Color endValue, float duration) {
            return target.DOColor(endValue, duration);
        }

        public static Tweener DOFade(SpriteRenderer target, float endValue, float duration) {
            return target.DOFade(endValue, duration);
        }

        public static Tweener DOMove(Transform target, Vector3 endValue, float duration) {
            return target.DOMove(endValue, duration);
        }

        public static Tweener DOMoveX(Transform target, float endValue, float duration) {
            return target.DOMoveX(endValue, duration);
        }

        public static Tweener DOMoveY(Transform target, float endValue, float duration) {
            return target.DOMoveY(endValue, duration);
        }

        public static Tweener DOMoveZ(Transform target, float endValue, float duration) {
            return target.DOMoveZ(endValue, duration);
        }

        public static Tweener DOScale(Transform target, Vector3 endValue, float duration) {
            return target.DOScale(endValue, duration);
        }

        public static Tweener DOScaleX(Transform target, float endValue, float duration) {
            return target.DOScaleX(endValue, duration);
        }

        public static Tweener DOScaleY(Transform target, float endValue, float duration) {
            return target.DOScaleY(endValue, duration);
        }

        public static Tweener DOScaleZ(Transform target, float endValue, float duration) {
            return target.DOScaleZ(endValue, duration);
        }

        public static Tweener DORotate(Transform target, Vector3 endValue, float duration) {
            return target.DORotate(endValue, duration);
        }

        public static Tweener DOIntensity(Light target, float endValue, float duration) {
            return target.DOIntensity(endValue, duration);
        }

        public static Tween OnComplete(Tweener tweener, System.Action callback) {
            return tweener.OnComplete(() => callback());
        }

        public static Tween OnComplete(Tweener tweener, UnityEngine.Events.UnityAction callback) {
            return tweener.OnComplete(() => callback());
        }

        /// <summary>
        /// 带回调的 DOFade，避免依赖有问题的 OnComplete 扩展方法
        /// </summary>
        public static Tweener DOFadeWithCallback(CanvasGroup target, float endValue, float duration, UnityEngine.Events.UnityAction callback) {
            return target.DOFade(endValue, duration).OnComplete(() => callback());
        }
    }
}