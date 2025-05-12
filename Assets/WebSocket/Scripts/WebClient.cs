using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.UI;
using WebSocketSharp;

public class WebClient : MonoBehaviour
{
    private const string URL = "ws://localhost:7272";
    
    public int width = 640;
    public int height = 480;
    public RawImage webcamImage;
    public RawImage receivedImage;
    
    private WebCamTexture webcamTexture;
    private RenderTexture renderTexture;
    private Texture2D captureTexture;
    private Texture2D receivedTexture;

    private WebSocket websocket;
    
    private ConcurrentQueue<byte[]> receivedDataQueue = new();

    private void Start()
    {
        CreateInstance();// 웹소켓을 통한 실시간 웹캠 스트리밍 시스템의 기반
        CreateWebSocket();// WebSocket 연결을 설정하고 관리
        
        StartCoroutine(SendProcess());
        StartCoroutine(ReceiveProcess());
        StartCoroutine(ReconnectProcess());
    }

    private void CreateInstance()
    {
        // 웹캠 텍스쳐를 지정된 너비와 높이로 생성
        webcamTexture = new WebCamTexture(width, height);
        // `Play()`를 호출하여 웹캠 캡처 시작
        webcamTexture.Play();
        
        // 웹캠 이미지를 표시를 위한 `RenderTexture`생성 
        renderTexture = new RenderTexture(width, height, 0);
        // `renderTexture`를 UI의 `RawImage`에 연결
        webcamImage.texture = renderTexture;
        
        // 'captureTexture' 웹캠 이미지를 인코딩하기 위한 '텍스처2D' 생성
        captureTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        // 'receivedTexture' 수신된 이미지를 표시하기 위한 '텍스처2D' 생성
        receivedTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        // 'receivedImage'를 UI의 'RawImage'에 연결   
        receivedImage.texture = receivedTexture;
    }

    private void CreateWebSocket()
    {
        websocket = new WebSocket(URL);
        websocket.OnOpen += (sender, e) => {
            Debug.Log("WebSocket Connected");
            StartCoroutine(SendProcess());
        };
        
        websocket.OnClose += (sender, e) => {
            Debug.LogWarning("WebSocket Disconnected: " + e.Reason);
        };
        
        websocket.OnMessage += (sender, e) => {
            if (e.IsBinary)
            {
                OnReceiveTexture(e.RawData);
            }
        };
        websocket.ConnectAsync();
    }

    private void OnReceiveTexture(byte[] bytes)
    {
        Debug.Log($"Receive Texture : {bytes.Length}");
        receivedDataQueue.Enqueue(bytes);
    }

    private IEnumerator SendProcess()
    {
        while (true)
        {
            // webcamTexture가 업데이트 될 때까지 대기
            yield return new WaitUntil(() => webcamTexture.didUpdateThisFrame);

            // webcamTexture를 renderTexture로 복사
            Graphics.Blit(webcamTexture, renderTexture);

            if (websocket.IsAlive)
            {
                // RenderTexture 내용을 captureTexture로 복사
                RenderTexture.active = renderTexture;
                captureTexture.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                captureTexture.Apply();
                RenderTexture.active = null;
                
                // jpg로 인코딩 후 전송
                var bytes = captureTexture.EncodeToJPG(70);
                if (bytes != null)
                {
                    Debug.Log($"Send Texture : {bytes.Length}");
                    websocket.Send(bytes);
                }
            }

            yield return null;
        }
    }

    private IEnumerator ReceiveProcess()
    {
        while (true)
        {
            yield return null;
            
            if (!websocket.IsAlive)
                continue;

            while (receivedDataQueue.Count > 0)
            {
                if (receivedDataQueue.TryDequeue(out var bytes))
                {
                    receivedTexture.LoadImage(bytes);
                    receivedTexture.Apply();
                }
            }
        }
    }

    private IEnumerator ReconnectProcess()
    {
        var wfs = new WaitForSeconds(1f);
        while (true)
        {
            yield return wfs;

            if (websocket.IsAlive)
                continue;
            
            Debug.Log("Reconnecting...");
            websocket.ConnectAsync();
        }
    }

    private void OnApplicationQuit()
    {
        if (websocket != null && websocket.IsAlive)
        {
            websocket.Close(CloseStatusCode.Normal, "Application Quit");
        }

        StopAllCoroutines();
    }
}