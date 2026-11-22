; Nyxia - three-image slideshow, CBM Prg Studio / C64.
; PAL: 250 frames/image. NTSC: 300 frames/image (approximately five seconds).
; Bank 0: screen $0400, sprites $0C00, code $1000, bitmap $2000.
; Image (112,16), 96x42. Raster 109 switches to blank blue text.
; $3800-$3FFF is both unused bitmap memory and a blank charset.

* = $0801
        BYTE $0B,$08,$0A,$00,$9E,$34,$30,$39,$36,$00,$00,$00
* = $1000
Start
        sei
        cld
        ldx #$FF
        txs
        lda #$2F
        sta $00
        lda #$35
        sta $01
        lda #<NmiHandler
        sta $FFFA
        lda #>NmiHandler
        sta $FFFB
        lda #$7F
        sta $DC0D
        sta $DD0D
        lda $DC0D
        lda $DD0D
        lda #$00
        sta $D01A
        sta $D015
        sta $D011
        sta ImageIndex
        lda $DD02
        ora #$03
        sta $DD02
        lda $DD00
        ora #$03
        sta $DD00
        lda #$06
        sta $D020
        sta $D021
        lda #$08
        sta $D016
        lda #$1E
        sta $D018

; Detect a raster beyond 287: PAL has 312 lines, NTSC has 262/263.
WaitRasterHigh
        lda $D011
        bpl WaitRasterHigh
DetectVideo
        lda $D012
        cmp #$20
        bcs PalVideo
        lda $D011
        bmi DetectVideo
        lda #$2C
        sta DurationLow
        lda #$01
        sta DurationHigh
        jmp InitMemory
PalVideo
        lda #$FA
        sta DurationLow
        lda #$00
        sta DurationHigh
InitMemory
        lda DurationLow
        sta FramesLow
        lda DurationHigh
        sta FramesHigh
        ldx #$00
        lda #$00
ClearBitmap
        sta $2000,x
        sta $2100,x
        sta $2200,x
        sta $2300,x
        sta $2400,x
        sta $2500,x
        sta $2600,x
        sta $2700,x
        sta $2800,x
        sta $2900,x
        sta $2A00,x
        sta $2B00,x
        sta $2C00,x
        sta $2D00,x
        sta $2E00,x
        sta $2F00,x
        sta $3000,x
        sta $3100,x
        sta $3200,x
        sta $3300,x
        sta $3400,x
        sta $3500,x
        sta $3600,x
        sta $3700,x
        sta $3800,x
        sta $3900,x
        sta $3A00,x
        sta $3B00,x
        sta $3C00,x
        sta $3D00,x
        sta $3E00,x
        sta $3F00,x
        inx
        bne ClearBitmap
        ldx #$00
        lda #$66
ClearScreen
        sta $0400,x
        sta $0500,x
        sta $0600,x
        sta $0700,x
        inx
        bne ClearScreen
        lda #$5B
        ldx #$00
ImageColours
        sta $045E,x
        sta $0486,x
        sta $04AE,x
        sta $04D6,x
        sta $04FE,x
        sta $0526,x
        inx
        cpx #$0C
        bne ImageColours
        lda #$00
        sta $D010
        sta $D017
        sta $D01B
        sta $D01C
        sta $D01D
        lda #$30
        sta $07F8
        lda #$88
        sta $D000
        lda #$42
        sta $D001
        lda #$0D
        sta $D027
        lda #$31
        sta $07F9
        lda #$A0
        sta $D002
        lda #$42
        sta $D003
        lda #$0D
        sta $D028
        lda #$32
        sta $07FA
        lda #$B8
        sta $D004
        lda #$42
        sta $D005
        lda #$0D
        sta $D029
        lda #$33
        sta $07FB
        lda #$D0
        sta $D006
        lda #$42
        sta $D007
        lda #$0D
        sta $D02A
        lda #$34
        sta $07FC
        lda #$88
        sta $D008
        lda #$57
        sta $D009
        lda #$0D
        sta $D02B
        lda #$35
        sta $07FD
        lda #$A0
        sta $D00A
        lda #$57
        sta $D00B
        lda #$0D
        sta $D02C
        lda #$36
        sta $07FE
        lda #$B8
        sta $D00C
        lda #$57
        sta $D00D
        lda #$0D
        sta $D02D
        lda #$37
        sta $07FF
        lda #$D0
        sta $D00E
        lda #$57
        sta $D00F
        lda #$0D
        sta $D02E
        jsr CopyImage0
        lda #$FF
        sta $D015
        lda #<BitmapIrq
        sta $FFFE
        lda #>BitmapIrq
        sta $FFFF
        lda #$30
        sta $D012
        lda #$1B
        sta $D011
        lda #$0F
        sta $D019
        lda #$01
        sta $D01A
        cli
Forever
        jmp Forever

BitmapIrq
        pha
        lda #$3B
        sta $D011
        lda #$6D
        sta $D012
        lda #<TextIrq
        sta $FFFE
        lda #>TextIrq
        sta $FFFF
        lda #$01
        sta $D019
        pla
        rti

TextIrq
        pha
        lda #$1B
        sta $D011
        txa
        pha
; Count displayed frames. All copying happens below the image, before next frame.
        lda FramesLow
        bne DecrementLow
        dec FramesHigh
DecrementLow
        dec FramesLow
        lda FramesLow
        ora FramesHigh
        bne FinishTextIrq
        inc ImageIndex
        lda ImageIndex
        cmp #$03
        bne SelectImage
        lda #$00
        sta ImageIndex
SelectImage
        cmp #$00
        beq ShowImage0
        cmp #$01
        beq ShowImage1
        jsr CopyImage2
        jmp ResetTimer
ShowImage0
        jsr CopyImage0
        jmp ResetTimer
ShowImage1
        jsr CopyImage1
ResetTimer
        lda DurationLow
        sta FramesLow
        lda DurationHigh
        sta FramesHigh
FinishTextIrq
        lda #$30
        sta $D012
        lda #<BitmapIrq
        sta $FFFE
        lda #>BitmapIrq
        sta $FFFF
        lda #$01
        sta $D019
        pla
        tax
        pla
        rti
NmiHandler
        rti
ImageIndex
        BYTE $00
FramesLow
        BYTE $00
FramesHigh
        BYTE $00
DurationLow
        BYTE $00
DurationHigh
        BYTE $00

; Regenerate after image conversion using Export-Images.ps1 in this folder.
        incasm "images.asm"
