using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

// 이 스크립트를 사용하는 오브젝트에는 AudioSource가 반드시 필요하다는 의미
[RequireComponent(typeof(AudioSource))]
public class MicrophoneTest : MonoBehaviour
{
    private AudioSource audioSource; // 마이크 입력을 받을 AudioSource 컴포넌트
    public Image soundImage; // 소리 크기를 시각화할 UI Image (fillAmount로 사용)

    void Start()
    {
        audioSource = GetComponent<AudioSource>(); // AudioSource 컴포넌트를 가져옴

        // 마이크 장치가 있는지 확인
        if (Microphone.devices.Length > 0)
        {
            string mic = Microphone.devices[0]; // 첫 번째 마이크 장치를 사용
            Debug.Log("사용중인 마이크 : " + mic);

            // 마이크 입력을 AudioSource에 연결 (10초짜리 루프 녹음, 샘플레이트 44100Hz)
            audioSource.clip = Microphone.Start(mic, true, 10, 44100);
            audioSource.loop = true; // 반복 재생 설정

            // 마이크가 시작될 때까지 대기
            while (!(Microphone.GetPosition(mic) > 0)) { }

            // 오디오 재생 시작 (실제로는 마이크 입력 소리 재생)
            audioSource.Play();
        }
        else
        {
            Debug.LogWarning("마이크 장치를 찾을수 없습니다");
        }
    }

    void Update()
    {
        float[] samples = new float[256]; // 오디오 샘플 데이터를 담을 배열
        audioSource.GetOutputData(samples, 0); // 현재 재생 중인 오디오의 데이터를 가져옴 (채널 0)

        float sum = 0f;
        // RMS(Root Mean Square)를 계산하기 위한 제곱합 계산
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        float rms = Mathf.Sqrt(sum / samples.Length); // RMS 계산 (소리의 에너지)
        float db = 20 * Mathf.Log10(rms / 0.1f); // RMS 값을 데시벨(dB)로 변환

        // 1단계: 너무 작은 소리는 무시하기 위한 임계값 설정
        float thresholdDb = -20f;
        db = Mathf.Max(db, thresholdDb); // 데시벨이 -20보다 작으면 -20으로 고정 (노이즈 방지)

        // 2단계: 데시벨을 0~1 범위로 정규화하고 감도 적용
        float sensitivity = 1.0f; // 감도 설정 (값이 클수록 민감)
        float normalizedVolume = Mathf.Clamp01(sensitivity * Mathf.InverseLerp(-20f, 0f, db));
        // Mathf.InverseLerp: -20dB ~ 0dB 범위를 0~1로 매핑
        // Clamp01: 값이 0보다 작거나 1보다 크지 않도록 제한

        // 3단계: UI 이미지에 부드럽게 반영 (볼륨 바처럼 보이게 하기 위해 보간 적용)
        float currentFill = soundImage.fillAmount;
        soundImage.fillAmount = Mathf.Lerp(currentFill, normalizedVolume, Time.deltaTime * 10f);
        // Mathf.Lerp를 사용해 갑작스러운 변화 없이 자연스럽게 fillAmount를 변경
    }
}