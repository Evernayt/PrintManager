using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

public class AnimatedPictureBox : PictureBox
{
    private Timer animationTimer;
    private int currentFrameIndex;

    public AnimatedPictureBox()
    {
        // Используем двойную буферизацию для более плавной анимации
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        // Рисуем текущий кадр изображения
        if (Image != null && Image.RawFormat.Equals(ImageFormat.Gif))
        {
            pe.Graphics.DrawImage(Image, 0, 0, Width, Height);
        }
        else
        {
            base.OnPaint(pe);
        }
    }

    // Запускаем анимацию
    public void StartAnimation()
    {
        // Проверяем, запущена ли уже анимация
        if (animationTimer != null && animationTimer.Enabled)
        {
            return; // Если запущена, ничего не делаем
        }

        if (Image != null && Image.RawFormat.Equals(ImageFormat.Gif))
        {
            currentFrameIndex = 0;

            // Создаем таймер только если он не создан
            if (animationTimer == null)
            {
                animationTimer = new Timer();
                animationTimer.Tick += AnimationTimer_Tick;
            }

            animationTimer.Interval = GetFrameDelay(currentFrameIndex);
            animationTimer.Start();
        }
    }

    // Останавливаем анимацию
    public void StopAnimation()
    {
        if (animationTimer != null)
        {
            animationTimer.Stop();
            animationTimer.Dispose();
            animationTimer = null;
        }
    }

    // Обработчик события таймера для смены кадров
    private void AnimationTimer_Tick(object sender, EventArgs e)
    {
        currentFrameIndex++;

        // Переходим к первому кадру, если дошли до конца
        if (currentFrameIndex >= Image.GetFrameCount(FrameDimension.Time))
        {
            currentFrameIndex = 0;
        }

        // Отображаем следующий кадр
        Image.SelectActiveFrame(FrameDimension.Time, currentFrameIndex);
        Invalidate(); // Перерисовываем PictureBox

        // Задаем интервал таймера равным задержке следующего кадра
        animationTimer.Interval = GetFrameDelay(currentFrameIndex);
    }

    // Получаем задержку кадра в миллисекундах
    private int GetFrameDelay(int frameIndex)
    {
        // Получаем задержку кадра из метаданных GIF
        // (обычно хранится в сотых долях секунды)
        int delay = Image.GetFrameCount(FrameDimension.Time) > 1 ?
            BitConverter.ToInt32(Image.GetPropertyItem(20736).Value, frameIndex * 4) * 10 : 100;

        return delay;
    }
}