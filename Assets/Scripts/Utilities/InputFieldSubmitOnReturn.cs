using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_InputField))]
public class InputFieldSubmitOnReturn : MonoBehaviour
{
    public StringEvent SubmitEvent;
    private TMP_InputField _inputField;

    private void Start() {
        _inputField = GetComponent<TMP_InputField>();
        _inputField.onEndEdit.AddListener(fieldValue => {
            if (Input.GetButton("Submit")) ValidateAndSubmit(fieldValue);
        });
    }

    private void ValidateAndSubmit (string text) {
        // validation if wanted
        SubmitEvent.Invoke(text);
        EventSystem.current.SetSelectedGameObject(null); // deselect
    }

    // used for buttons
    public void ValidateAndSubmit () {
        ValidateAndSubmit(_inputField.text);
    }

}

[Serializable]
public class StringEvent : UnityEvent<string> { }