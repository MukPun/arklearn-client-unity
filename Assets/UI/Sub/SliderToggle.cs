using DG.Tweening; // 引入DOTween命名空间
using UnityEngine;
using UnityEngine.UI;
 
public class SwitchToggle : MonoBehaviour
{
    [Header("滑块引用")]
    public RectTransform handleRect; // 拖拽赋值：Handle对象的RectTransform

    [Header("背景设置")]
    public GameObject bkOn; // 开启状态背景（开启时显示，关闭时隐藏）
    public GameObject bkOff; // 关闭状态背景（关闭时显示，开启时隐藏）

    [Header("动画设置")]
    public float slideDuration = 0.3f; // 滑动动画时长
    public Ease slideEase = Ease.InOutBack; // 滑动缓动类型，提供弹性效果
 
    private Vector2 onPosition; // 滑块在“开”状态的位置
    private Vector2 offPosition; // 滑块在“关”状态的位置
    private Toggle toggle; // 本体的Toggle组件
 
    private void Awake()
    {
        // 获取组件引用
        toggle = GetComponent<Toggle>();
 
        // 安全检查
        if (handleRect == null)
        {
            Debug.LogError("请为SwitchToggleController的handleRect赋值！", this);
            return;
        }
 
        // 记录初始位置作为“开”状态位置
        offPosition = handleRect.anchoredPosition;
        // 计算“关”状态位置：假设向左滑动，x坐标取反
        onPosition = new Vector2(-offPosition.x, offPosition.y);
 
        // 监听Toggle状态变化
        toggle.onValueChanged.AddListener(OnToggleValueChanged);
 
        // 初始化，根据当前Toggle状态设置滑块位置
        UpdateHandlePosition(toggle.isOn, true); // 立即设置，无动画

        // 初始化背景显示状态
        UpdateBackgroundVisibility(toggle.isOn, true);
    }

    private void OnToggleValueChanged(bool isOn)
    {
        // 当Toggle值改变时，以动画形式更新滑块位置
        UpdateHandlePosition(isOn, false);
        UpdateBackgroundVisibility(isOn, false);
    }

    private void UpdateBackgroundVisibility(bool isOn, bool immediate)
    {
        if (bkOn != null)
        {
            bkOn.SetActive(isOn);
        }
        if (bkOff != null)
        {
            bkOff.SetActive(!isOn);
        }
    }
 
    private void UpdateHandlePosition(bool isOn, bool immediate)
    {
        Vector2 targetPos = isOn ? onPosition : offPosition;
 
        if (immediate)
        {
            // 立即设置位置（用于初始化）
            handleRect.anchoredPosition = targetPos;
        }
        else
        {
            // 使用DOTween进行动画移动
            // Kill任何正在进行的该滑块动画，防止冲突
            handleRect.DOKill();
            // 执行锚点位置动画
            handleRect.DOAnchorPos(targetPos, slideDuration)
                     .SetEase(slideEase);
        }
    }
 
    private void OnDestroy()
    {
        // 销毁时移除监听，防止内存泄漏
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
        }
        // 安全地销毁与此对象相关的DOTween动画
        if (handleRect != null)
        {
            handleRect.DOKill();
        }
    }
}
