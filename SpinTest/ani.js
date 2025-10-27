console.clear();

// 創建音效對象
const soundEffect = new Audio('se_door_hanekaeri.mp3');
const rotateSound = new Audio('cada3.MP3');

// 創建震動函數
function triggerVibration(pattern = [100, 50, 100]) {
  console.log("嘗試觸發震動，模式:", pattern);
  console.log("navigator.vibrate 存在:", "vibrate" in navigator);
  console.log("navigator.vibrate 函數:", typeof navigator.vibrate);
  console.log("用戶代理:", navigator.userAgent);
  console.log("是否為移動設備:", /Android|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent));
  
  // 更安全的檢查方式
  if ("vibrate" in navigator) {
    try {
      // 檢查是否為移動設備
      const isMobile = /Android|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
      console.log("檢測到移動設備:", isMobile);
      
      if (isMobile) {
        navigator.vibrate(pattern);
        console.log("移動設備震動觸發:", pattern);
      } else {
        console.log("桌面設備，震動功能不可用");
      }
    } catch (error) {
      console.log("震動執行失敗:", error);
    }
  } else {
    console.log("此設備或瀏覽器不支援震動功能");
  }
}

// 確保 DOM 完全載入後再初始化
$(document).ready(function() {
  console.log("DOM 已載入，開始初始化 Draggable...");
  
  // 檢查必要的庫是否已載入
  if (typeof gsap === 'undefined') {
    console.error("GSAP 未載入！");
    return;
  }
  
  if (typeof Draggable === 'undefined') {
    console.error("Draggable 插件未載入！");
    return;
  }
  
  // 檢查旋鈕元素是否存在
  const knobElement = document.getElementById("knob");
  if (!knobElement) {
    console.error("找不到 #knob 元素！");
    return;
  }
  
  console.log("開始創建 Draggable...");
  
  // 追蹤已播放的角度區間
  let lastPlayedAngle = 0;
  const ROTATE_THRESHOLD = 15; // 每15度觸發一次音效
  
  // 創建可拖拽的旋鈕
  const draggable = Draggable.create("#knob", {
    type: "rotation",
    inertia: true,
    onDragStart: function() {
      // 記錄開始拖拽時的角度
      this.startAngle = this.rotation;
      lastPlayedAngle = this.rotation; // 初始化已播放的角度
      console.log("開始拖拽，起始角度:", this.startAngle);
    },
    onDrag: function() {
      let currentAngle = this.rotation;
      let rotationDelta = currentAngle - this.startAngle;
      
      // 檢查是否經過了15度的增量
      let angleDifference = Math.abs(currentAngle - lastPlayedAngle);
      
      // 如果旋轉超過15度，播放音效並更新已播放角度
      if (angleDifference >= ROTATE_THRESHOLD) {
        rotateSound.currentTime = 0; // 重置音效
        rotateSound.play().catch(e => console.log("旋轉音效播放失敗:", e));
        lastPlayedAngle = currentAngle; // 更新已播放角度
        console.log(`達到15度增量，播放音效。當前角度: ${currentAngle.toFixed(1)}°`);
      }
      
      // 實時更新角度顯示
      updateAngleDisplay();
      
      // 限制旋轉增量在 90 度以內
      if (Math.abs(rotationDelta) > 45) {
        // 如果旋轉超過 45 度，就限制到 90 度
        let targetAngle = this.startAngle + (rotationDelta > 0 ? 90 : -90);
        
        // 強制設定到限制的角度，使用回彈動畫
        gsap.to(this.target, {
          rotation: targetAngle,
          duration: 0.4,
          ease: "elastic.out(1, 0.4)"
        });
        
        // 播放回彈音效
        soundEffect.currentTime = 0; // 重置音效到開始位置
        soundEffect.play().catch(e => console.log("音效播放失敗:", e));
        
        // 稍微延遲震動，讓聲音先開始播放
        setTimeout(() => {
          triggerVibration(200); // 設備震動200毫秒
        }, 50); // 延遲50ms讓聲音先開始
        
        lastPlayedAngle = targetAngle; // 更新已播放角度
        
        console.log(`旋轉被限制到: ${targetAngle}° (增量: ${rotationDelta.toFixed(1)}°)`);
      } else {
        console.log("拖拽中，旋轉角度:", this.rotation, "增量:", rotationDelta.toFixed(1));
      }
    },
    onDragEnd: function() {
      // 拖拽結束時進行最終吸附
      let currentAngle = this.rotation;
      let rotationDelta = currentAngle - this.startAngle;
      
      // 計算最終角度（限制在 90 度增量內）
      let finalAngle = this.startAngle;
      if (Math.abs(rotationDelta) > 45) {
        finalAngle = this.startAngle + (rotationDelta > 0 ? 90 : -90);
      } else {
        // 如果增量小於 45 度，回到起始角度
        finalAngle = this.startAngle;
      }
      
      // 使用強烈的回彈動畫吸附到最終角度
      gsap.to(this.target, {
        rotation: finalAngle,
        duration: 0.6,
        ease: "elastic.out(1, 0.3)"
      });

      // 如果最終角度與起始角度不同，播放回彈音效和震動
      if (finalAngle !== this.startAngle) {
        soundEffect.currentTime = 0; // 重置音效到開始位置
        soundEffect.play().catch(e => console.log("音效播放失敗:", e));
        
        // 稍微延遲震動，讓聲音先開始播放
        setTimeout(() => {
          triggerVibration(200); // 設備震動200毫秒
        }, 0); // 延遲50ms讓聲音先開始
      }
      
      // 更新已播放角度為最終角度
      lastPlayedAngle = finalAngle;

      console.log(`拖拽結束，最終角度: ${finalAngle}° (增量: ${rotationDelta.toFixed(1)}°)`);
      
      // 更新最終角度顯示
      updateAngleDisplay();
    }
  });
  
  console.log("Draggable 創建成功:", draggable);
  
  // 更新角度顯示的函數
  function updateAngleDisplay() {
    const currentRotation = gsap.getProperty("#knob", "rotation");
    $("#angleDisplay").text("當前角度: " + currentRotation.toFixed(1) + "°");
  }
  
  // 初始化角度顯示
  updateAngleDisplay();
  
  // 綁定震動測試按鈕
  $("#vibrateTest").click(function() {
    console.log("震動測試按鈕被點擊");
    console.log("=== 震動診斷開始 ===");
    
    // 測試不同的震動模式
    console.log("測試1: 簡單震動 200ms");
    triggerVibration(200);
    
    setTimeout(() => {
      console.log("測試2: 震動模式 [100, 50, 100]");
      triggerVibration([100, 50, 100]);
    }, 1000);
    
    setTimeout(() => {
      console.log("測試3: 強烈震動 500ms");
      triggerVibration(500);
    }, 2000);
    
    console.log("=== 震動診斷結束 ===");
  });
  
  console.log("初始化完成！");
});
