﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class SentenceDisplayer : MonoBehaviour
{
    public UiNote UiNotePrefab;
    private AudioSource m_audioSource;

    private SongMeta m_songMeta;
    
    private int m_sentenceIndex;
    private Voice m_voice;
    private Sentence m_sentence;

    private SSingController m_ssingController;

    void Start() {
        // Reduced update frequency.
        InvokeRepeating("UpdateCurrentSentence", 0, 0.25f);
    }

    void UpdateCurrentSentence() {
        if(m_songMeta == null || m_voice == null || m_sentence == null) {
            return;
        }

        if(m_ssingController == null) {
            m_ssingController = FindObjectOfType<SSingController>();
        }

        // Change the sentence, when the current beat is over its last note.
        if(m_voice.Sentences.Count > m_sentenceIndex - 1) {
            if((uint)m_ssingController.CurrentBeat > m_sentence.EndBeat) {
                m_sentenceIndex++;
                LoadCurrentSentence();
            } else {
                // Debug.Log("Current beat: "+(uint)m_ssingController.CurrentBeat);
            }
        }
    }

    public void LoadVoice(SongMeta songMeta, string voiceIdentifier) {
        m_songMeta = songMeta;

        string filePath = m_songMeta.Directory + Path.DirectorySeparatorChar + m_songMeta.Filename;
        Debug.Log($"Loading voice of {filePath}");
        var voices = VoicesBuilder.ParseFile(filePath, m_songMeta.Encoding, new List<string>());
        if(string.IsNullOrEmpty(voiceIdentifier)) {
            m_voice = voices.Values.First();
        } else {
            if(!voices.TryGetValue(voiceIdentifier, out m_voice)) {
                throw new Exception($"The song does not contain a voice for {voiceIdentifier}");
            }
        }

        m_sentenceIndex = 0;
        LoadCurrentSentence();
    }

    private void LoadCurrentSentence() {
        m_sentence = m_voice.Sentences[m_sentenceIndex];
        DisplayCurrentNotes();

        var sentenceTexts = m_sentence.Notes.Select(it => it.Text);
        Debug.Log("Loaded sentence: "+string.Join(" ", sentenceTexts));
        Debug.Log($"End beat: {m_sentence.EndBeat}");
    }

    private void DisplayCurrentNotes()
    {
        foreach(UiNote uiNote in GetComponentsInChildren<UiNote>()) {
            Destroy(uiNote.gameObject);
        }

        foreach(var note in m_sentence.Notes) {
            DisplayNote(note);
        }
    }

    private void DisplayNote(Note note)
    {
        UiNote uiNote = Instantiate(UiNotePrefab);
        uiNote.transform.SetParent(transform);

        var uiNoteText = uiNote.GetComponentInChildren<Text>();
        uiNoteText.text = note.Text;
    }
}
