using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle), typeof(Animator))]
public class ToggleAnimSync : MonoBehaviour
{
    [SerializeField] private string isOnParam = "IsOn";
    private Toggle _toggle;
    private Animator _anim;
    private int _isOnHash;

    void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _anim = GetComponent<Animator>();
        _isOnHash = Animator.StringToHash(isOnParam);
        
        // 初始化状态
        _anim.SetBool(_isOnHash, _toggle.isOn);
        // 绑定Toggle事件
        _toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    void OnEnable()
    {
        // 确保每次看到界面的时候都能同步Toggle状态
        _isOnHash = Animator.StringToHash(isOnParam);
        _anim.SetBool(_isOnHash, _toggle.isOn);
    }


    void OnToggleChanged(bool isOn)
    {
        _anim.SetBool(_isOnHash, isOn);
    }

    void OnDestroy()
    {
        _toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }
}