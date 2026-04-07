using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(ContentSizeFitter))]
public class MaxWidthLimiter : MonoBehaviour
{
    [SerializeField] private float maxWidth = 200f;

    private ContentSizeFitter _fitter;
    private RectTransform _rect;

    void Awake()
    {
        _fitter = GetComponent<ContentSizeFitter>();
        _rect = GetComponent<RectTransform>();
    }

    void Update()
    {
        if (_rect.sizeDelta.x > maxWidth)
        {
            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxWidth);
        }
    }
}