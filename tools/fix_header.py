import io

p = 'HHCL/Views/ModDetailView.xaml'
s = io.open(p, encoding='utf-8').read()

# 1) 头部加"全部收起/展开"按钮
old = '''                        <TextBlock Grid.Column="1" x:Name="VersionCountText" Style="{StaticResource Label}" VerticalAlignment="Center" />'''
new = '''                        <StackPanel Grid.Column="1" Orientation="Horizontal">
                            <TextBlock x:Name="VersionCountText" Style="{StaticResource Label}" VerticalAlignment="Center" />
                            <Button x:Name="CollapseAllBtn" Content="全部收起" Style="{StaticResource GhostButton}"
                                    Height="26" Padding="10,0" Margin="10,0,0,0" VerticalAlignment="Center"
                                    Click="CollapseAll_Click" />
                        </StackPanel>'''
assert old in s, 'count text anchor'
s = s.replace(old, new, 1)

# 2) 组头：改成可点击的按钮（带折叠箭头）
old2 = '''                                    <!-- 组头内容 -->
                                    <Border Background="#1AFFFFFF" CornerRadius="5" Padding="10,6">
                                        <Border.Style>
                                            <Style TargetType="Border">
                                                <Setter Property="Visibility" Value="Collapsed" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding IsHeader}" Value="True">
                                                        <Setter Property="Visibility" Value="Visible" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Border.Style>
                                        <StackPanel Orientation="Horizontal">
                                            <TextBlock Text="{Binding Name}" FontSize="13.5" FontWeight="SemiBold"
                                                       Foreground="{StaticResource AccentLightBrush}" />
                                            <TextBlock Text="{Binding Size}" FontSize="11" Margin="10,2,0,0"
                                                       Style="{StaticResource Label}" VerticalAlignment="Center" />
                                        </StackPanel>
                                    </Border>'''
new2 = '''                                    <!-- 组头：整行可点击，折叠/展开这一组 -->
                                    <Button Click="Header_Click">
                                        <Button.Style>
                                            <Style TargetType="Button">
                                                <Setter Property="Background" Value="#1AFFFFFF" />
                                                <Setter Property="BorderThickness" Value="0" />
                                                <Setter Property="Padding" Value="10,6" />
                                                <Setter Property="Cursor" Value="Hand" />
                                                <Setter Property="Visibility" Value="Collapsed" />
                                                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding IsHeader}" Value="True">
                                                        <Setter Property="Visibility" Value="Visible" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Button.Style>
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="Auto" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <TextBlock Grid.Column="0" Text="&#x25BE;" FontSize="11"
                                                       Margin="0,1,8,0" VerticalAlignment="Center"
                                                       Foreground="{StaticResource TextSecondaryBrush}" />
                                            <TextBlock Grid.Column="1" Text="{Binding Name}" FontSize="13.5" FontWeight="SemiBold"
                                                       Foreground="{StaticResource AccentLightBrush}" />
                                            <TextBlock Grid.Column="2" Text="{Binding Size}" FontSize="11" Margin="10,2,0,0"
                                                       Style="{StaticResource Label}" VerticalAlignment="Center" />
                                        </Grid>
                                    </Button>'''
assert old2 in s, 'header template anchor'
s = s.replace(old2, new2, 1)

# 3) 组头必须能点，去掉 IsHitTestVisible=False
old3 = '''                                                <DataTrigger Binding="{Binding IsHeader}" Value="True">
                                                    <Setter Property="Margin" Value="2,16,2,5" />
                                                    <Setter Property="IsHitTestVisible" Value="False" />
                                                </DataTrigger>'''
new3 = '''                                                <DataTrigger Binding="{Binding IsHeader}" Value="True">
                                                    <Setter Property="Margin" Value="2,16,2,5" />
                                                </DataTrigger>'''
assert old3 in s, 'grid style anchor'
s = s.replace(old3, new3, 1)

io.open(p, 'w', encoding='utf-8').write(s)
print('XAML 已改：组头可点击 + 全部收起按钮')
